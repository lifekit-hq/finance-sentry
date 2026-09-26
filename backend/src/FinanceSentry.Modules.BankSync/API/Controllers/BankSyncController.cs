namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.API.Extensions;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.API.Validation;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

[ApiController]
[Route("accounts")]
public class BankSyncController(
    ICommandHandler<ConnectMonobankAccountCommand, ConnectMonobankResult> connectMonobankHandler,
    IQueryHandler<GetAccountsQuery, GetAccountsResult> getAccountsHandler,
    IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult> allTransactionsHandler,
    IQueryHandler<ListTrueLayerProvidersQuery, IReadOnlyList<TrueLayerProviderDto>> listTrueLayerProvidersHandler,
    ICommandHandler<BeginTrueLayerConnectCommand, BeginTrueLayerConnectResult> beginTrueLayerConnectHandler,
    ICommandHandler<FinalizeTrueLayerConnectCommand, FinalizeTrueLayerConnectResult> finalizeTrueLayerConnectHandler,
    ICommandHandler<DisconnectInstitutionCommand, DisconnectInstitutionResult> disconnectInstitutionHandler,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<BankSyncController> logger,
    IBankAccountRepository accounts,
    ITransactionRepository transactions,
    IBackgroundJobClient backgroundJobs,
    ISyncJobRepository syncJobs,
    ITransactionSyncCoordinator coordinator,
    FinanceSentry.Core.Interfaces.IAlertGeneratorService alerts) : ControllerBase
{
    private readonly IBankAccountRepository _accounts = accounts;
    private readonly ITransactionRepository _transactions = transactions;
    private readonly IBackgroundJobClient _backgroundJobs = backgroundJobs;
    private readonly ISyncJobRepository _syncJobs = syncJobs;
    private readonly IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult> _allTransactionsHandler = allTransactionsHandler;
    private readonly ITransactionSyncCoordinator _coordinator = coordinator;

    // ── GET /api/accounts ── T207 ────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAccounts(
        [FromQuery] string? status = null,
        [FromQuery] string? currency = null,
        CancellationToken ct = default)
    {
        var result = await getAccountsHandler.Handle(
            new GetAccountsQuery(User.RequireUserId(), status, currency), ct);

        return Ok(result);
    }

    // ── GET /api/accounts/transactions ── T208-G ─────────────────────────────

    private const int MinTransactionsLimit = 1;
    private const int MaxTransactionsLimit = 200;

    [HttpGet("transactions")]
    public async Task<IActionResult> GetAllTransactions(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? transactionType = null,
        [FromQuery] List<Guid>? accountId = null,
        [FromQuery] List<string>? category = null,
        [FromQuery] decimal? minAmountUsd = null,
        [FromQuery] decimal? maxAmountUsd = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var fromDateError = TransactionFilterValidator.ValidateDate(from, out var fromDate);
        if (fromDateError != null)
            return BadRequest(fromDateError);
        var toDateError = TransactionFilterValidator.ValidateDate(to, out var toDate);
        if (toDateError != null)
            return BadRequest(toDateError);

        var fromDateTime = fromDate?.ToDateTime(TimeOnly.MinValue);
        var toDateTime = toDate?.ToDateTime(TimeOnly.MaxValue);

        var validationError = TransactionFilterValidator.ValidateDateRange(fromDateTime, toDateTime)
            ?? TransactionFilterValidator.ValidateAmountRange(minAmountUsd, maxAmountUsd)
            ?? TransactionFilterValidator.ValidateCategories(category)
            ?? TransactionFilterValidator.ValidateTransactionType(transactionType)
            ?? TransactionFilterValidator.ValidateSearch(search);
        if (validationError != null)
            return BadRequest(validationError);

        limit = Math.Clamp(limit, MinTransactionsLimit, MaxTransactionsLimit);

        var result = await _allTransactionsHandler.Handle(
            new GetAllTransactionsQuery(
                User.RequireUserId(),
                new PagedRequest(offset, limit),
                fromDateTime,
                toDateTime,
                transactionType,
                accountId,
                category,
                minAmountUsd,
                maxAmountUsd,
                search), ct);

        return Ok(PaginationExtensions.CreatePaginatedResponse(
            result.Transactions, result.TotalCount, result.Offset, result.Limit));
    }

    // ── GET /api/accounts/{accountId}/transactions ── T208 ───────────────────

    [HttpGet("{accountId:guid}/transactions")]
    public async Task<IActionResult> GetTransactions(
        Guid accountId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? transactionType = null,
        [FromQuery] List<string>? category = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] decimal? maxAmount = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var userId = User.RequireUserId();

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account == null || account.UserId != userId)
            return NotFound(new ApiErrorBody("Account not found.", "ACCOUNT_NOT_FOUND"));

        var fromDateError = TransactionFilterValidator.ValidateDate(from, out var fromDate);
        if (fromDateError != null)
            return BadRequest(fromDateError);
        var toDateError = TransactionFilterValidator.ValidateDate(to, out var toDate);
        if (toDateError != null)
            return BadRequest(toDateError);

        var fromDateTime = fromDate?.ToDateTime(TimeOnly.MinValue);
        var toDateTime = toDate?.ToDateTime(TimeOnly.MaxValue);

        var validationError = TransactionFilterValidator.ValidateDateRange(fromDateTime, toDateTime)
            ?? TransactionFilterValidator.ValidateAmountRange(minAmount, maxAmount)
            ?? TransactionFilterValidator.ValidateCategories(category)
            ?? TransactionFilterValidator.ValidateTransactionType(transactionType)
            ?? TransactionFilterValidator.ValidateSearch(search);
        if (validationError != null)
            return BadRequest(validationError);

        limit = Math.Clamp(limit, MinTransactionsLimit, MaxTransactionsLimit);

        var filter = new TransactionFilter(
            Categories: category,
            From: fromDateTime,
            To: toDateTime,
            TransactionType: transactionType,
            Search: search,
            MinAmount: minAmount,
            MaxAmount: maxAmount);

        var (txList, totalCount) = await _transactions.GetFilteredByAccountIdAsync(accountId, filter, offset, limit, ct);

        var items = txList.Select(t => new TransactionDto(
            t.Id,
            t.AccountId,
            t.Amount,
            t.TransactionDate,
            t.PostedDate,
            t.Description,
            t.TransactionType,
            t.MerchantCategory,
            t.IsPending,
            t.CreatedAt,
            account.Provider,
            account.AccountType
        )).ToList();

        return Ok(new TransactionPageResponse(
            account.Id.ToString(),
            account.BankName,
            account.Currency,
            items,
            totalCount,
            offset,
            limit,
            (offset + items.Count) < totalCount));
    }

    // ── POST /api/accounts/{accountId}/sync ── T308 ──────────────────────────

    [HttpPost("{accountId:guid}/sync")]
    public async Task<IActionResult> TriggerSync(Guid accountId, CancellationToken ct)
    {
        var userId = User.RequireUserId();

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account == null || account.UserId != userId)
            return NotFound(new ApiErrorBody("Account not found.", "ACCOUNT_NOT_FOUND"));

        if (await _syncJobs.HasRunningJobAsync(accountId, ct))
            return Conflict(new ApiErrorBody("A sync is already in progress for this account.", "SYNC_IN_PROGRESS"));

        var hangfireJobId = _backgroundJobs.Enqueue<Infrastructure.Jobs.ScheduledSyncJob>(
            job => job.ExecuteSyncAsync(accountId));

        return Accepted(new SyncEnqueuedResponse(
            hangfireJobId,
            "Sync enqueued. Use GET /api/accounts/{accountId}/sync-status to track progress."));
    }

    // ── GET /api/accounts/{accountId}/sync-status ── T309 ───────────────────

    [HttpGet("{accountId:guid}/sync-status")]
    public async Task<IActionResult> GetSyncStatus(Guid accountId, CancellationToken ct)
    {
        var userId = User.RequireUserId();

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account == null || account.UserId != userId)
            return NotFound(new ApiErrorBody("Account not found.", "ACCOUNT_NOT_FOUND"));

        var latestJob = await _syncJobs.GetLatestByAccountIdAsync(accountId, ct);
        if (latestJob == null)
            return Ok(new SyncStatusResponse("never_synced", 0, 0, null, null, null, null));

        return Ok(new SyncStatusResponse(
            latestJob.Status,
            latestJob.TransactionCountFetched,
            latestJob.TransactionCountDeduped,
            latestJob.ErrorMessage,
            latestJob.CompletedAt,
            latestJob.StartedAt,
            latestJob.CompletedAt));
    }

    // ── DELETE /api/v1/accounts/institutions/{provider}/{institutionId} ──
    // Institution-level disconnect: removes a Monobank credential or TrueLayer
    // connection and cascades to every child sub-account, transaction, and alert.

    [HttpDelete("institutions/{provider}/{institutionId:guid}")]
    public async Task<IActionResult> DisconnectInstitution(string provider, Guid institutionId, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        try
        {
            var result = await disconnectInstitutionHandler.Handle(
                new DisconnectInstitutionCommand(userId, provider, institutionId), ct);
            return Ok(new { removedAccounts = result.RemovedAccounts });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiErrorBody(ex.Message, "INSTITUTION_NOT_FOUND"));
        }
    }

    // ── DELETE /api/accounts/{accountId} ── T309-A ───────────────────────────

    [HttpDelete("{accountId:guid}")]
    public async Task<IActionResult> DeleteAccount(Guid accountId, CancellationToken ct)
    {
        var userId = User.RequireUserId();

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account == null || account.UserId != userId)
            return NotFound(new ApiErrorBody("Account not found.", "ACCOUNT_NOT_FOUND"));

        await _transactions.SoftDeleteByAccountIdAsync(accountId, ct);
        await _accounts.DeleteAsync(accountId, ct);
        await alerts.DeleteAlertsForAccountAsync(accountId, ct);

        return NoContent();
    }

    // ── POST /api/accounts/monobank/connect ── T019 ──────────────────────────

    [HttpPost("monobank/connect")]
    public async Task<IActionResult> ConnectMonobank(
        [FromBody] ConnectMonobankRequest request, CancellationToken ct)
    {
        var result = await connectMonobankHandler.Handle(
            new ConnectMonobankAccountCommand(User.RequireUserId(), request.Token), ct);

        return StatusCode(201, result);
    }

    // ── GET /api/v1/accounts/truelayer/providers?country=ie ──────────────────

    [HttpGet("truelayer/providers")]
    public async Task<IActionResult> ListTrueLayerProviders(
        [FromQuery] string? country = "ie", CancellationToken ct = default)
    {
        _ = User.RequireUserId();
        var result = await listTrueLayerProvidersHandler.Handle(
            new ListTrueLayerProvidersQuery(country), ct);
        return Ok(result);
    }

    // ── POST /api/v1/accounts/truelayer/connect ──────────────────────────────

    [HttpPost("truelayer/connect")]
    public async Task<IActionResult> BeginTrueLayerConnect(
        [FromBody] BeginTrueLayerConnectRequest request, CancellationToken ct)
    {
        var result = await beginTrueLayerConnectHandler.Handle(
            new BeginTrueLayerConnectCommand(
                User.RequireUserId(), request.ProviderId, request.ProviderName), ct);
        return Ok(result);
    }

    // ── GET /api/v1/accounts/truelayer/callback?code=&state=&error= ──────────
    //
    // Public endpoint hit by TrueLayer after the user consents at their bank.
    // Exempt from JWT auth; identifies the connection by the 'state' parameter.

    [HttpGet("truelayer/callback")]
    public async Task<IActionResult> TrueLayerCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct = default)
    {
        var frontendBase = (configuration["TrueLayer:FrontendRedirectBase"]
            ?? "http://localhost:4200").TrimEnd('/');

        if (!string.IsNullOrEmpty(error))
            return Redirect($"{frontendBase}/accounts/list?connectError={Uri.EscapeDataString(error)}");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Redirect($"{frontendBase}/accounts/list?connectError=MISSING_CODE_OR_STATE");

        try
        {
            await finalizeTrueLayerConnectHandler.Handle(
                new FinalizeTrueLayerConnectCommand(state, code), ct);
            return Redirect($"{frontendBase}/accounts/list?connected=truelayer");
        }
        catch (FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer.TrueLayerException ex)
        {
            logger.LogError(ex, "TrueLayer callback finalization failed: code={ErrorCode}, message={Message}",
                ex.ErrorCode, ex.Message);
            return Redirect($"{frontendBase}/accounts/list?connectError={Uri.EscapeDataString(ex.ErrorCode)}");
        }
    }
}
