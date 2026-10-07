import {type SignInMethods} from '../models/auth/auth.model';

export type AuthFlow = 'login' | 'acceptInvite' | null;

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
  status: AsyncStatus;
  errorCode: Nullable<string>;
  errorDetail: Nullable<string>;
  flow: AuthFlow;
  returnUrl: Nullable<string>;
  flashMessage: Nullable<FlashMessage>;
  /** Null until `auth/methods` answers; the login pages render their method choices only once it is set. */
  signInMethods: Nullable<SignInMethods>;
}

export const initialAuthState: AuthState = {
  userId: null,
  email: null,
  firstName: null,
  lastName: null,
  roles: [],
  permissions: [],
  status: 'idle',
  errorCode: null,
  errorDetail: null,
  flow: null,
  returnUrl: null,
  flashMessage: null,
  signInMethods: null,
};
