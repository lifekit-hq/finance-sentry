/** Derived on the server from the account: no password and no Google link yet, signed up, or revoked. */
export type PersonStatus = 'Invited' | 'Active' | 'Revoked';

export interface Person {
  id: string;
  email: string;
  roles: string[];
  status: PersonStatus;
}

/** A People-list row: the person plus what the page derives for display. */
export interface PersonRow extends Person {
  rolesLabel: string;
  canRevoke: boolean;
}

export interface CreateInviteRequest {
  email: string;
}

/** A freshly created invite. The token is shown once, inside the link the owner sends by hand. */
export interface Invite {
  userId: string;
  email: string;
  token: string;
  expiresAt: string;
}
