import {type SignInMethods} from '../../models/auth/auth.model';

/** What the login page offers when `auth/methods` cannot be read: the pre-OIDC behaviour. */
export const FALLBACK_SIGN_IN_METHODS: SignInMethods = {
  oidc: false,
  passwordLogin: true,
  googleDirect: true,
};
