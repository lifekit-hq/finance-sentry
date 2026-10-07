import {type SignInMethods} from '../../models/auth/auth.model';

/** The `info` query param /login carries after a sign-out; it keeps the page from forwarding straight back to the identity provider. */
export const SIGNED_OUT_INFO = 'signed_out';

/** What the login page offers when `auth/methods` cannot be read: the pre-OIDC behaviour. */
export const FALLBACK_SIGN_IN_METHODS: SignInMethods = {
  oidc: false,
  passwordLogin: true,
};
