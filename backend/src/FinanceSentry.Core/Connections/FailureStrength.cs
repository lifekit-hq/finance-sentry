namespace FinanceSentry.Core.Connections;

/// <summary>How sure a classification is; decides whether one occurrence is enough (report §5).</summary>
public enum FailureStrength
{
    /// <summary>The provider said so plainly; one occurrence is enough.</summary>
    Definitive,

    /// <summary>Looks like the class, but the provider sends it for other reasons too; needs a confirmation.</summary>
    Suspect,
}
