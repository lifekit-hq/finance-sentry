import {type Nullable} from '@lifekit-hq/ui';

import {type SignInMethods} from '../models/auth/auth.model';

export interface FlashMessage {
  kind: 'info' | 'error';
  text: string;
}

export interface AuthState {
  userId: Nullable<string>;
  email: Nullable<string>;
  firstName: Nullable<string>;
  lastName: Nullable<string>;
  roles: string[];
  permissions: string[];
  returnUrl: Nullable<string>;
  flashMessage: Nullable<FlashMessage>;
  /** Null until `auth/methods` answers; the login pages render their method choices only once it is set. */
  signInMethods: Nullable<SignInMethods>;
  /** False until the first session call (`auth/me` at start) answers, whichever way; the shell draws before that. */
  sessionResolved: boolean;
  /** The protected URL a navigation reached while the session call was out; null once it settles. */
  deferredAuthUrl: Nullable<string>;
}

export const initialAuthState: AuthState = {
  userId: null,
  email: null,
  firstName: null,
  lastName: null,
  roles: [],
  permissions: [],
  returnUrl: null,
  flashMessage: null,
  signInMethods: null,
  sessionResolved: false,
  deferredAuthUrl: null,
};
