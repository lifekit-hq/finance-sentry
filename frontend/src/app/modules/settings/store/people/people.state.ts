import {type Invite, type Person} from '../../models/person/person.model';

export interface PeopleState {
  people: Person[];
  status: AsyncStatus;
  errorCode: Nullable<string>;
  inviteStatus: AsyncStatus;
  inviteErrorCode: Nullable<string>;
  lastInvite: Nullable<Invite>;
  revokingId: Nullable<string>;
  revokeErrorCode: Nullable<string>;
}

export const initialPeopleState: PeopleState = {
  people: [],
  status: 'idle',
  errorCode: null,
  inviteStatus: 'idle',
  inviteErrorCode: null,
  lastInvite: null,
  revokingId: null,
  revokeErrorCode: null,
};
