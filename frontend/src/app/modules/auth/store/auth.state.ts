export type AuthFlow = 'login' | 'acceptInvite' | 'google' | null;

export interface FlashMessage {
  kind: 'info' | 'error';
  text: string;
}

export interface AuthState {
  userId: Nullable<string>;
  email: Nullable<string>;
  roles: string[];
  permissions: string[];
  status: AsyncStatus;
  errorCode: Nullable<string>;
  flow: AuthFlow;
  returnUrl: Nullable<string>;
  flashMessage: Nullable<FlashMessage>;
}

export const initialAuthState: AuthState = {
  userId: null,
  email: null,
  roles: [],
  permissions: [],
  status: 'idle',
  errorCode: null,
  flow: null,
  returnUrl: null,
  flashMessage: null,
};
