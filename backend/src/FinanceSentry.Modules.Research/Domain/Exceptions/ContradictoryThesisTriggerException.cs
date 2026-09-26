namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class ContradictoryThesisTriggerException(string metric, string direction)
    : ApiException(
        422,
        "CONTRADICTORY_THESIS_TRIGGER",
        $"Thesis carries more than one invalidation trigger for metric '{metric}' with direction '{direction}' " +
        "— keep a single threshold per metric and direction.");
