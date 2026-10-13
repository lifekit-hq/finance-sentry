namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// What kind of lifecycle subject a <see cref="ThesisEvent"/> is about. <c>Candidate</c> paths
/// are 019 candidates; <c>Recommendation</c> is a #1069 proposal put to the owner.
/// </summary>
public enum ThesisSubjectType
{
    Thesis,
    Candidate,
    Recommendation,
}
