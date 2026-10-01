import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type Invite, type Person} from '../../models/person/person.model';
import {peopleMethods} from './people.methods';
import {initialPeopleState} from './people.state';

const PERSON: Person = {id: 'u-1', email: 'friend@test.com', roles: ['Member'], status: 'Invited'};
const INVITE: Invite = {
  userId: 'u-1',
  email: 'friend@test.com',
  token: 'one-time',
  expiresAt: '2099-01-01T00:00:00Z',
};

function makeState() {
  const state = signalState(initialPeopleState);
  return {state, methods: peopleMethods(state)};
}

describe('peopleMethods', () => {
  it('setLoading marks the list loading and clears a previous error', () => {
    const {state, methods} = makeState();
    methods.setLoadError('X');

    methods.setLoading();

    expect(state.status()).toBe('loading');
    expect(state.errorCode()).toBeNull();
  });

  it('setPeople stores the list and returns to idle', () => {
    const {state, methods} = makeState();
    methods.setLoading();

    methods.setPeople([PERSON]);

    expect(state.people()).toEqual([PERSON]);
    expect(state.status()).toBe('idle');
  });

  it('setLoadError records the code', () => {
    const {state, methods} = makeState();

    methods.setLoadError('FORBIDDEN');

    expect(state.status()).toBe('error');
    expect(state.errorCode()).toBe('FORBIDDEN');
  });

  it('setInviting clears the previous invite and error', () => {
    const {state, methods} = makeState();
    methods.setInvite(INVITE);
    methods.setInviteError('DUPLICATE_EMAIL');

    methods.setInviting();

    expect(state.inviteStatus()).toBe('loading');
    expect(state.inviteErrorCode()).toBeNull();
    expect(state.lastInvite()).toBeNull();
  });

  it('setInvite keeps the invite for the one-time link', () => {
    const {state, methods} = makeState();
    methods.setInviting();

    methods.setInvite(INVITE);

    expect(state.inviteStatus()).toBe('idle');
    expect(state.lastInvite()).toEqual(INVITE);
  });

  it('setInviteError records the code', () => {
    const {state, methods} = makeState();

    methods.setInviteError('DUPLICATE_EMAIL');

    expect(state.inviteStatus()).toBe('error');
    expect(state.inviteErrorCode()).toBe('DUPLICATE_EMAIL');
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
});
