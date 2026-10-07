import {type ErrorMessagesMap} from '@lifekit-hq/core';

export const ERROR_MESSAGES_REGISTRY: ErrorMessagesMap = {
  GOOGLE_ACCOUNT_ONLY: "This account uses Google sign-in. Click 'Continue with Google' instead.",
  DUPLICATE_EMAIL: 'This email already belongs to an account.',
  INVALID_INVITE: 'This invite link is invalid or has expired. Ask the owner for a new one.',
  SIGN_IN_METHOD_DISABLED: 'This sign-in method is not available.',
  ACCOUNT_NOT_INVITED: 'No account exists for this email. Ask the owner for an invite.',
  ACCOUNT_UNAVAILABLE:
    'This account is locked or has been revoked. Try again later or ask the owner.',
  OIDC_FAILED: 'Sign-in failed. Please try again.',
  CANNOT_REVOKE_SELF: 'You cannot revoke your own access.',
  CANNOT_REVOKE_OWNER: "The owner's access cannot be revoked.",
  USER_NOT_FOUND: 'That person no longer exists.',
  MONOBANK_TOKEN_INVALID: 'Invalid Monobank token. Please check and try again.',
  MONOBANK_TOKEN_DUPLICATE: 'This Monobank token is already connected.',
  MONOBANK_RATE_LIMITED: 'Monobank rate limit reached. Please wait 60 seconds and try again.',
  BINANCE_INVALID_CREDENTIALS:
    'Binance rejected the provided credentials. Use a read-only API key with no IP restrictions.',
  BINANCE_DUPLICATE:
    'Binance account already connected. Disconnect the existing one to use new keys.',
  BINANCE_ALREADY_CONNECTED:
    'Binance account already connected. Disconnect the existing one to use new keys.',
  REVOLUT_X_INVALID_CREDENTIALS:
    "Revolut X rejected the key pair. Check the API key belongs to the public key you registered, paste the Ed25519 private key (not the public one), and allow Finance Sentry's server IP if the key is IP-restricted.",
  REVOLUT_X_ALREADY_CONNECTED:
    'Revolut X account already connected. Disconnect the existing one to use a new key.',
  IBKR_INVALID_CREDENTIALS:
    'IB Gateway rejected the provided credentials. Confirm the 2FA push notification on your phone and try again.',
  IBKR_DUPLICATE: 'IBKR account already connected. Disconnect the existing one to reconnect.',
  IBKR_ALREADY_CONNECTED:
    'IBKR account already connected. Disconnect the existing one to reconnect.',
  IBKR_FLEX_INVALID_TOKEN:
    "IBKR didn't accept this token. Copy it again from Flex Web Service Configuration, with no spaces — a token stops working once a new one is generated.",
  IBKR_FLEX_TOKEN_EXPIRED:
    'This token has expired. Generate a new one in Flex Web Service Configuration (pick the 1 year expiry) and paste it here.',
  IBKR_FLEX_QUERY_NOT_FOUND:
    "IBKR couldn't find a Flex query with that ID. Copy the Query ID from the list of Activity Flex Queries — it's the number beside your query's name, not the token.",
  IBKR_FLEX_NOT_READY:
    'IBKR is still preparing the report. This is normal — wait a minute and press Check connection again.',
  IBKR_FLEX_RATE_LIMITED: 'IBKR limits how often a token can be used. Wait a minute and try again.',
  IBKR_FLEX_IP_RESTRICTED:
    'This token is restricted to specific IP addresses. In Flex Web Service Configuration remove the IP restriction, then try again.',
  IBKR_FLEX_ERROR:
    'IBKR could not run this Flex query. Check the token and query ID, and that it is an Activity Flex Query.',
  IBKR_GATEWAY_UNAVAILABLE:
    'Could not reach the IBKR gateway. This is usually temporary — try again in a minute.',
  INZHUR_CREDENTIALS_REQUIRED: 'Enter the phone number and password you use for Inzhur.',
  INZHUR_LOGIN_LIMIT:
    'Inzhur sign-in is limited to two attempts a day, as each one can send you an SMS. Try again tomorrow.',
  INZHUR_RECAPTCHA_REJECTED:
    "Inzhur's bot check turned this sign-in away. Wait a while and try again; your saved holdings stay as they are.",
  INZHUR_INVALID_CREDENTIALS:
    "Inzhur didn't accept this phone number and password. Check them in the Inzhur app and enter them again.",
  INZHUR_CHALLENGE_EXPIRED: 'The SMS code has expired. Start over to get a new one.',
  INZHUR_TOO_MANY_ATTEMPTS: 'Too many wrong codes. Start over to get a new one.',
  INZHUR_INVALID_CODE: "That code didn't match. Check the SMS from Inzhur and try again.",
  INZHUR_LOGIN_UNAVAILABLE:
    "Finance Sentry can't sign in to Inzhur right now. Try again later; your saved holdings stay as they are.",
  INZHUR_LOGIN_FAILED: "Couldn't sign in to Inzhur. Please try again.",
  INZHUR_NOT_CONNECTED: 'Inzhur is not connected.',
  VALIDATION_ERROR: 'Some fields look wrong — please review the highlighted errors.',
  ALERT_NOT_FOUND: 'Alert not found.',
  PUSH_UNAVAILABLE: 'Push notifications are not available right now.',
  PUSH_SUBSCRIPTION_INVALID: 'This device could not be registered for push notifications.',
  PUSH_SUBSCRIPTION_NOT_FOUND: 'That device is no longer registered.',
  PUSH_PERMISSION_DENIED:
    'Notifications are blocked for this site. Allow them in your browser settings, then try again.',
  PUSH_SUBSCRIBE_FAILED: 'This browser could not be set up for push notifications. Try again.',
  ALERT_LOAD_FAILED: 'Failed to load alerts.',
  EVENTS_WINDOW_INVALID: 'The events window is invalid. Pick a range of at most a year.',
  EVENTS_KINDS_INVALID: 'One of the requested event kinds is not recognised.',
  BUDGET_NOT_FOUND: 'Budget not found.',
  BUDGET_DUPLICATE_CATEGORY: 'A budget for this category already exists.',
  BUDGET_INVALID_CATEGORY: 'Invalid budget category.',
  BUDGET_INVALID_LIMIT: 'Budget limit must be greater than zero.',
  BUDGET_INVALID_PERIOD: 'Invalid budget period.',
  SUBSCRIPTION_NOT_FOUND: 'Subscription not found.',
  FORBIDDEN: 'This feature is not available for your account.',
  LEDGER_READ_UNAVAILABLE: 'Ledger could not produce a read right now. Try again shortly.',
  INVALID_DATE_RANGE: 'Invalid date range. Check the from/to dates and try again.',
  INVALID_AMOUNT_RANGE: 'Invalid amount range. Check the min/max amounts and try again.',
  INVALID_CATEGORY: 'One of the selected categories is not recognized.',
  INVALID_TRANSACTION_TYPE: 'Invalid transaction type. Use debit or credit.',
  INVALID_SEARCH: 'Search text is too long.',
  COMMITMENT_TRANSACTION_NOT_FOUND: 'That transaction is no longer available. Pick another one.',
  COMMITMENT_ALREADY_TRACKED: 'Charges like this one are already tracked on this page.',
  COMMITMENT_ALREADY_LINKED: 'This row already follows its transactions.',
  INVALID_COMMITMENT_KIND: 'Choose subscription or installment.',
  INVALID_COMMITMENT_CADENCE: 'Choose monthly or yearly.',
  // Feature 040 agent error codes are lowercase snake_case by contract (chat-endpoint.md).
  /* eslint-disable @typescript-eslint/naming-convention */
  agent_not_configured:
    'Ledger is not configured. An Anthropic API key must be set on the server to enable chat.',
  llm_unavailable: 'Ledger is temporarily unavailable. Please try again in a moment.',
  conversation_not_found: 'Conversation not found.',
  /* eslint-enable @typescript-eslint/naming-convention */
};
