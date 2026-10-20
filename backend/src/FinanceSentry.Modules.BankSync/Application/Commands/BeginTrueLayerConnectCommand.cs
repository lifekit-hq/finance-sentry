namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using Microsoft.Extensions.Options;

public record BeginTrueLayerConnectRequest(string ProviderId, string? ProviderName);

public record BeginTrueLayerConnectCommand(
    Guid UserId,
    string ProviderId,
    string? ProviderName) : ICommand<BeginTrueLayerConnectResult>;

public record BeginTrueLayerConnectResult(string Link, string Reference);

public class BeginTrueLayerConnectCommandHandler(
    ITrueLayerClient client,
    ITrueLayerConnectionRepository connections,
    IOptions<TrueLayerOptions> options)
    : ICommandHandler<BeginTrueLayerConnectCommand, BeginTrueLayerConnectResult>
{
    public async Task<BeginTrueLayerConnectResult> Handle(
        BeginTrueLayerConnectCommand request, CancellationToken cancellationToken)
    {

        var reference = Guid.NewGuid().ToString("N");
        var redirectUri = options.Value.RedirectUri;

        var link = client.BuildAuthLink(request.ProviderId, reference, redirectUri);

        // A connection for this provider may already exist — either healthy (accidental
        // re-connect) or in a reauth_required state after its refresh token died. In both cases
        // we reuse the existing row rather than blocking or deleting it: keeping the same
        // connection id means the linked accounts stay attached and get healed in place on
        // finalize, and leaving the old refresh token intact means an abandoned consent does not
        // sever a still-working connection. Only when no connection exists do we create a new one.
        var existing = await connections.GetByUserAndProviderAsync(
            request.UserId, request.ProviderId, cancellationToken);

        if (existing != null)
        {
            existing.BeginReauth(reference);
            await connections.UpdateAsync(existing, cancellationToken);
        }
        else
        {
            var entity = new TrueLayerConnection(
                userId: request.UserId,
                providerId: request.ProviderId,
                providerDisplayName: request.ProviderName ?? request.ProviderId,
                reference: reference);

            await connections.AddAsync(entity, cancellationToken);
        }

        return new BeginTrueLayerConnectResult(Link: link, Reference: reference);
    }
}
