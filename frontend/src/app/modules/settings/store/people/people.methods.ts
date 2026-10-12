import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type Invite, type Person} from '../../models/person/person.model';
import {type PeopleState} from './people.state';

export function peopleMethods(store: WritableStateSource<PeopleState>) {
  return {
    setPeople(people: Person[]): void {
      patchState(store, {people});
    },
    setInvite(invite: Invite): void {
      patchState(store, {lastInvite: invite});
    },
    dismissInvite(): void {
      patchState(store, {lastInvite: null});
    },
    setRevoking(userId: string): void {
      patchState(store, {revokingId: userId, revokeErrorCode: null});
    },
    setRevoked(): void {
      patchState(store, {revokingId: null, revokeErrorCode: null});
    },
    setRevokeError(errorCode: Nullable<string>): void {
      patchState(store, {revokingId: null, revokeErrorCode: errorCode});
    },
  };
}
