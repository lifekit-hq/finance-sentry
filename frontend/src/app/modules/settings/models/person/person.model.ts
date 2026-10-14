/** Derived on the server from the account: not signed in yet, signed in, revoked, or closed by its owner. */
export type PersonStatus = 'Invited' | 'Active' | 'Revoked' | 'Closed';

export interface Person {
  id: string;
  email: string;
  roles: string[];
  status: PersonStatus;
  /** When a closed account's data is erased unless an owner reopens it first; null otherwise. */
  purgeDueAt?: Nullable<string>;
}

/** A People-list row: the person plus what the page derives for display. */
export interface PersonRow extends Person {
  rolesLabel: string;
  /** The roles, plus when a closed account's data is erased. */
  sublabel: string;
  canRevoke: boolean;
  canReopen: boolean;
}

export interface CreateInviteRequest {
  email: string;
}

/** A freshly created invite: the person signs in through the identity provider with this email. */
export interface Invite {
  userId: string;
  email: string;
}
