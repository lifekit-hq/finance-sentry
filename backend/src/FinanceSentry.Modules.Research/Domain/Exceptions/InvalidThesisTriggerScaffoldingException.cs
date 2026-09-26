namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class InvalidThesisTriggerScaffoldingException(string metric, string reason)
    : ApiException(
        422,
        "INVALID_THESIS_TRIGGER_SCAFFOLDING",
        $"Invalidation trigger for metric '{metric}' is missing required scaffolding ({reason}) and cannot be evaluated.");
