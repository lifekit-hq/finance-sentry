import {type UserProfile} from '../../../settings/models/settings/settings.model';

/** Which sign-in methods the API offers (`GET auth/methods`). */
export interface SignInMethods {
  /** The org identity provider (OIDC) login is configured. */
  oidc: boolean;
}

/** Body of `auth/logout` when the identity provider's session must end too; absent (204) otherwise. */
export interface LogoutResponse {
  endSessionUrl: string;
}

export interface UserDto {
  id: string;
  email: string;
  roles: string[];
  /** Effective permission claims (see `Permission`); UX gating only, the API enforces them. */
  permissions: string[];
}

export interface AuthResponse {
  user: UserDto;
  expiresAt: string;
  profile?: UserProfile;
}
