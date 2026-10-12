/** Mirrors the backend's cap (`InzhurConnector.MaxRefreshCookieLength`); a longer paste is not a cookie. */
export const INZHUR_SESSION_MAX_LENGTH = 8192;

export const INZHUR_READ_ONLY_NOTE =
  'Finance Sentry only reads your portfolio once a day. It never trades, withdraws or changes anything in your cabinet.';

/**
 * How the owner hands over his session. Inzhur's sign-in turns this server away with a bot check,
 * so he signs in in his own desktop browser and copies the session's refresh cookie.
 */
export const INZHUR_SESSION_STEPS: readonly string[] = [
  'Sign in to your cabinet on inzhur.reit in a desktop browser.',
  'Open DevTools → Application → Cookies → https://api.inzhur.reit and copy the Value of refreshToken.',
  'Paste it below, then close the tab without signing out: signing out ends the session.',
];

export const INZHUR_SESSION_NOTE =
  'Stored encrypted and used only to refresh your own session. Finance Sentry never sees your phone or password.';
