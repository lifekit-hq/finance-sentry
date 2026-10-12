import {type Invite, type Person} from '../../models/person/person.model';

export interface PeopleState {
  people: Person[];
  lastInvite: Nullable<Invite>;
  revokingId: Nullable<string>;
  revokeErrorCode: Nullable<string>;
}

export const initialPeopleState: PeopleState = {
  people: [],
  lastInvite: null,
  revokingId: null,
  revokeErrorCode: null,
};
