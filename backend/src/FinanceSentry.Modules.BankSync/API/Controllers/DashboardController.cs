namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("dashboard")]
public class DashboardController(
    IDashboardQueryService dashboard,
    ITransactionRepository transactions,
    IBankAccountRepository accounts,
    ITransferDetectionService transferDetection,
    IFlowBreakdownService flowBreakdown,
    IUserBaseCurrencyReader baseCurrencyReader) : ControllerBase
{
    private readonly IUserBaseCurrencyReader _baseCurrencyReader = baseCurrencyReader ?? throw new ArgumentNullException(nameof(baseCurrencyReader));
    private readonly IDashboardQueryService _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    private readonly IFlowBreakdownService _flowBreakdown = flowBreakdown ?? throw new ArgumentNullException(nameof(flowBreakdown));
    private readonly ITransactionRepository _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ITransferDetectionService _transferDetection = transferDetection ?? throw new ArgumentNullException(nameof(transferDetection));

    // ── GET /api/dashboard/aggregated ── T408 ─────────────────────────────────

    [HttpGet("aggregated")]
    public async Task<IActionResult> GetAggregated(
        [FromQuery] int months = 6,
        [FromQuery] int? windowMonths = null,
        [FromQuery] DateOnly? windowFrom = null,
        CancellationToken ct = default)
    {
        var userId = User.RequireUserId();
        var data = await _dashboard.GetDashboardDataAsync(userId, months, windowMonths, windowFrom, ct);
        var baseCurrency = CurrencyConverter.ResolveBase(await _baseCurrencyReader.GetAsync(userId, ct));
        return Ok(InBaseCurrency(data, baseCurrency));
    }

    // The service speaks USD; the profile's base currency is applied once, here, with the same
    // rate table every other conversion uses. USD (or an unset profile) is an identity.
    private static DashboardData InBaseCurrency(DashboardData data, string baseCurrency)
    {
        if (baseCurrency == "USD")
            return data with { BaseCurrency = baseCurrency };

        decimal Fx(decimal usd) => CurrencyConverter.FromUsd(usd, baseCurrency);
        MonthlyFlow Flow(MonthlyFlow f) => f with
        {
            InflowUsd = Fx(f.InflowUsd),
            OutflowUsd = Fx(f.OutflowUsd),
            NetUsd = Fx(f.NetUsd),
            CommittedOutflowUsd = Fx(f.CommittedOutflowUsd),
            DiscretionaryOutflowUsd = Fx(f.DiscretionaryOutflowUsd),
            FamilySupportOutflowUsd = Fx(f.FamilySupportOutflowUsd),
            InvestedOutflowUsd = Fx(f.InvestedOutflowUsd),
        };

        return data with
        {
            TotalNetWorthUsd = Fx(data.TotalNetWorthUsd),
            MonthlyFlow = [.. data.MonthlyFlow.Select(Flow)],
            WindowFlow = data.WindowFlow?.Select(Flow).ToList(),
            TopCategories = [.. data.TopCategories.Select(c => c with { TotalSpend = Fx(c.TotalSpend) })],
            BaseCurrency = baseCurrency,
        };
    }

    // ── GET /api/dashboard/flow-breakdown ─────────────────────────────────────

    /// <summary>
    /// Every credit/debit of one month labelled with the bucket the flow statistics put it
    /// in — the audit view behind the dashboard tiles. <paramref name="months"/> must be the
    /// window the dashboard was rendered with so pair detection sees the same neighbours.
    /// <para>
    /// With <paramref name="from"/> and/or <paramref name="to"/> (open ends default to the
    /// first day ever and to today) the view is a UTC day range instead of a month — what the
    /// dashboard's windows drill into — and <paramref name="month"/> is ignored.
    /// </para>
    /// </summary>
    [HttpGet("flow-breakdown")]
    public async Task<IActionResult> GetFlowBreakdown(
        [FromQuery] string? month = null,
        [FromQuery] int months = 6,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        if (from is not null || to is not null)
        {
            var rangeFrom = from ?? DateOnly.MinValue;
            var rangeTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            if (rangeTo < rangeFrom)
                return BadRequest(new { errorCode = "INVALID_RANGE", message = "to must not be before from" });

            return Ok(await _flowBreakdown.GetRangeBreakdownAsync(User.RequireUserId(), rangeFrom, rangeTo, months, ct));
        }

        if (month is null || !DateTime.TryParseExact(
                month, "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            return BadRequest(new { errorCode = "INVALID_MONTH", message = "month must be yyyy-MM" });
        }

        var data = await _flowBreakdown.GetBreakdownAsync(User.RequireUserId(), month, months, ct);
        return Ok(data);
    }

    // ── GET /api/dashboard/transfers ── T410 ──────────────────────────────────

    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransfers(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var allTx = (await _transactions.GetByUserIdAsync(userId, ct)).ToList();
        var accountCurrencies = (await _accounts.GetByUserIdAsync(userId, ct))
            .ToDictionary(a => a.Id, a => a.Currency);
        var transferIds = _transferDetection.DetectTransferTransactionIds(allTx, accountCurrencies);

        var byId = allTx.ToDictionary(t => t.Id);
        var consumedCredits = new HashSet<Guid>();
        var pairs = new List<TransferPairDto>();

        foreach (var debit in allTx.Where(t => t.TransactionType == "debit" && transferIds.Contains(t.Id)))
        {
            foreach (var credit in allTx.Where(t => t.TransactionType == "credit" && transferIds.Contains(t.Id)))
            {
                if (consumedCredits.Contains(credit.Id)) continue;
                if (!_transferDetection.IsLikelyTransfer(
                        debit, credit,
                        accountCurrencies.GetValueOrDefault(debit.AccountId),
                        accountCurrencies.GetValueOrDefault(credit.AccountId))) continue;

                pairs.Add(new TransferPairDto(
                    new TransferItemDto(
                        debit.Id, debit.AccountId, debit.Amount,
                        debit.PostedDate ?? debit.TransactionDate, debit.Description),
                    new TransferItemDto(
                        credit.Id, credit.AccountId, credit.Amount,
                        credit.PostedDate ?? credit.TransactionDate, credit.Description)));
                consumedCredits.Add(credit.Id);
                break;
            }
        }

        return Ok(new TransferPairsResponse(pairs, pairs.Count));
    }
}
