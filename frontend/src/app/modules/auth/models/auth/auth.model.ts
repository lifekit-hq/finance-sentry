import {type UserProfile} from '../../../settings/models/settings/settings.model';

export interface AuthRequest {
  email: string;
  password: string;
}

/** Body of `auth/invite/accept`: the user id and one-time token from the invite link, plus the chosen password. */
export interface AcceptInviteRequest {
  userId: string;
  token: string;
  password: string;
}

/** Which sign-in methods the API offers (`GET auth/methods`). */
export interface SignInMethods {
  /** The org identity provider (OIDC) login is configured. */
  oidc: boolean;
  passwordLogin: boolean;
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
