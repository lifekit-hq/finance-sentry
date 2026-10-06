namespace FinanceSentry.Modules.Companion.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class PushUnavailableException()
    : ApiException(503, "PUSH_UNAVAILABLE", "Push notifications are not available on this server.");

public class PushSubscriptionInvalidException()
    : ApiException(400, "PUSH_SUBSCRIPTION_INVALID", "The push subscription is not valid.");

public class PushSubscriptionNotFoundException()
    : ApiException(404, "PUSH_SUBSCRIPTION_NOT_FOUND", "Push subscription not found.");
