/**
 * A wrong SMS code is a 200 from the backend (`invalid_code`); the strategy raises it under this
 * code so the connect store shows the registry's message like any other failure.
 */
export const INZHUR_INVALID_CODE = 'INZHUR_INVALID_CODE';

/** The backend's catch-all sign-in failure; also used for a failure that carries no code. */
export const INZHUR_LOGIN_FAILED = 'INZHUR_LOGIN_FAILED';

/** A phone as typed: digits with optional +, spaces, dashes and brackets (the server keeps digits only). */
export const INZHUR_PHONE_PATTERN = /^\s*\+?[\d\s()-]{9,20}$/;

/** Inzhur's SMS codes are short numeric codes; spaces from a pasted code are dropped. */
export const INZHUR_CODE_PATTERN = /^\s*(\d\s*){4,8}$/;

export const INZHUR_READ_ONLY_NOTE =
  'Finance Sentry only reads your portfolio once a day. It never trades, withdraws or changes anything in your cabinet.';

export const INZHUR_SMS_NOTE =
  'Inzhur will text a code to your phone. Each sign-in can send an SMS, so Finance Sentry allows two a day.';
