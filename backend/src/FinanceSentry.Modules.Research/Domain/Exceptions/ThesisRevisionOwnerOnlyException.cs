namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>A confirm or reject came from something other than the owner's own session (a service token, the in-app agent).</summary>
public class ThesisRevisionOwnerOnlyException()
    : ApiException(403, "THESIS_REVISION_OWNER_ONLY", "Only the owner confirms or rejects a thesis revision.");
