import {HttpContextToken} from '@angular/common/http';

/** Set by AuthService on its own calls: a 401 there is never answered with a refresh-and-retry. */
export const SKIP_AUTH_REFRESH = new HttpContextToken<boolean>(() => false);

/** Set on the session-restore GET: an anonymous visitor's expected 401 must not end the session. */
export const SESSION_PROBE = new HttpContextToken<boolean>(() => false);
