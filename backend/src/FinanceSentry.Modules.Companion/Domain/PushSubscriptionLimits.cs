namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>Column and request bounds for a stored Web Push subscription.</summary>
public static class PushSubscriptionLimits
{
    public const int EndpointMaxLength = 2048;

    public const int P256dhMaxLength = 128;

    public const int AuthMaxLength = 32;

    public const int DeviceLabelMaxLength = 100;
}
