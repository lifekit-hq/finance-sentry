import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type Invite, type Person} from '../../models/person/person.model';
import {peopleMethods} from './people.methods';
import {initialPeopleState} from './people.state';

const PERSON: Person = {id: 'u-1', email: 'friend@test.com', roles: ['Member'], status: 'Invited'};
const INVITE: Invite = {
  userId: 'u-1',
  email: 'friend@test.com',
};

function makeState() {
  const state = signalState(initialPeopleState);
  return {state, methods: peopleMethods(state)};
}

describe('peopleMethods', () => {
  it('setPeople stores the list', () => {
    const {state, methods} = makeState();

    methods.setPeople([PERSON]);

    expect(state.people()).toEqual([PERSON]);
  });

  it('setInvite keeps the invite for the one-time link', () => {
    const {state, methods} = makeState();

    methods.setInvite(INVITE);

    expect(state.lastInvite()).toEqual(INVITE);
  });

  it('dismissInvite forgets the link', () => {
    const {state, methods} = makeState();
    methods.setInvite(INVITE);

    methods.dismissInvite();

    expect(state.lastInvite()).toBeNull();
  });

  it('tracks a revoke from start to finish', () => {
    const {state, methods} = makeState();

    methods.setRevoking('u-1');
    expect(state.revokingId()).toBe('u-1');

    methods.setRevoked();
    expect(state.revokingId()).toBeNull();
    expect(state.revokeErrorCode()).toBeNull();
  });

  it('setRevokeError records the code and stops tracking the revoke', () => {
    const {state, methods} = makeState();
    methods.setRevoking('u-1');

    methods.setRevokeError('CANNOT_REVOKE_OWNER');

    expect(state.revokingId()).toBeNull();
    expect(state.revokeErrorCode()).toBe('CANNOT_REVOKE_OWNER');
  });

  it('tracks a reopen from start to finish', () => {
    const {state, methods} = makeState();

    methods.setReopening('u-1');
    expect(state.reopeningId()).toBe('u-1');

    methods.setReopened();
    expect(state.reopeningId()).toBeNull();
    expect(state.reopenErrorCode()).toBeNull();
  });

  it('setReopenError records the code and stops tracking the reopen', () => {
    const {state, methods} = makeState();
    methods.setReopening('u-1');

    methods.setReopenError('ACCOUNT_NOT_CLOSED');

    expect(state.reopeningId()).toBeNull();
    expect(state.reopenErrorCode()).toBe('ACCOUNT_NOT_CLOSED');
  });
});
