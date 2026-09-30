import {type UserProfile} from '../../../settings/models/settings/settings.model';

export interface AuthRequest {
  email: string;
  password: string;
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
