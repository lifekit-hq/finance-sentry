import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type Invite, type Person} from '../../models/person/person.model';
import {type PeopleState} from './people.state';

export function peopleMethods(store: WritableStateSource<PeopleState>) {
  return {
    setLoading(): void {
      patchState(store, {status: 'loading', errorCode: null});
    },
    setPeople(people: Person[]): void {
      patchState(store, {people, status: 'idle', errorCode: null});
    },
    setLoadError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
    setInviting(): void {
      patchState(store, {inviteStatus: 'loading', inviteErrorCode: null, lastInvite: null});
    },
    setInvite(invite: Invite): void {
      patchState(store, {inviteStatus: 'idle', inviteErrorCode: null, lastInvite: invite});
    },
    setInviteError(errorCode: Nullable<string>): void {
      patchState(store, {inviteStatus: 'error', inviteErrorCode: errorCode});
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
