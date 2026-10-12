/** Derived on the server from the account: not signed in yet, signed in, or revoked. */
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

/** A freshly created invite: the person signs in through the identity provider with this email. */
export interface Invite {
  userId: string;
  email: string;
}
