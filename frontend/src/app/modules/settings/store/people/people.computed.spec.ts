import {DOCUMENT} from '@angular/common';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {AuthStore} from '../../../auth/store/auth.store';
import {type Invite, type Person} from '../../models/person/person.model';
import {peopleComputed} from './people.computed';

const ORIGIN = 'https://app.example.test';
const CURRENT_USER_ID = 'owner-1';

function build(
  overrides: Partial<{
    people: Person[];
    status: AsyncStatus;
    errorCode: Nullable<string>;
    inviteStatus: AsyncStatus;
    inviteErrorCode: Nullable<string>;
    lastInvite: Nullable<Invite>;
    revokeErrorCode: Nullable<string>;
  }> = {}
) {
  return {
    people: signal<Person[]>(overrides.people ?? []),
    status: signal<AsyncStatus>(overrides.status ?? 'idle'),
    errorCode: signal<Nullable<string>>(overrides.errorCode ?? null),
    inviteStatus: signal<AsyncStatus>(overrides.inviteStatus ?? 'idle'),
    inviteErrorCode: signal<Nullable<string>>(overrides.inviteErrorCode ?? null),
    lastInvite: signal<Nullable<Invite>>(overrides.lastInvite ?? null),
    revokeErrorCode: signal<Nullable<string>>(overrides.revokeErrorCode ?? null),
  };
}

function compute(store: ReturnType<typeof build>) {
  return TestBed.runInInjectionContext(() => peopleComputed(store));
}

describe('peopleComputed', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {provide: AuthStore, useValue: {userId: signal(CURRENT_USER_ID)}},
        {provide: DOCUMENT, useValue: {location: {origin: ORIGIN}}},
      ],
    });
  });

  describe('isLoading', () => {
    it('is true while the first load runs', () => {
      expect(compute(build({status: 'loading'})).isLoading()).toBe(true);
    });

    it('is false on a reload that already has people to show', () => {
      const people: Person[] = [{id: 'u', email: 'a@test.com', roles: [], status: 'Active'}];
      expect(compute(build({status: 'loading', people})).isLoading()).toBe(false);
    });
  });

  it('isInviting follows the invite status', () => {
    expect(compute(build({inviteStatus: 'loading'})).isInviting()).toBe(true);
    expect(compute(build()).isInviting()).toBe(false);
  });

  describe('rows', () => {
    const people: Person[] = [
      {id: CURRENT_USER_ID, email: 'me@test.com', roles: ['Owner'], status: 'Active'},
      {id: 'owner-2', email: 'co-owner@test.com', roles: ['Owner'], status: 'Active'},
      {id: 'member', email: 'member@test.com', roles: ['Member'], status: 'Active'},
      {id: 'invited', email: 'invited@test.com', roles: ['Member'], status: 'Invited'},
      {id: 'revoked', email: 'revoked@test.com', roles: [], status: 'Revoked'},
    ];

    it('joins roles and labels a person without one', () => {
      const rows = compute(build({people})).rows();
      expect(rows.map(r => r.rolesLabel)).toEqual([
        'Owner',
        'Owner',
        'Member',
        'Member',
        'No role',
      ]);
    });

    it('offers revoke only for non-owner, non-self, not-yet-revoked people', () => {
      const rows = compute(build({people})).rows();
      expect(rows.filter(r => r.canRevoke).map(r => r.id)).toEqual(['member', 'invited']);
    });
  });

  describe('inviteLink', () => {
    it('is null without an invite', () => {
      expect(compute(build()).inviteLink()).toBeNull();
    });

    it('builds the accept-invite link on this origin', () => {
      const lastInvite: Invite = {
        userId: 'u-1',
        email: 'friend@test.com',
        token: 'a+b/c=',
        expiresAt: '2099-01-01T00:00:00Z',
      };

      const link = new URL(compute(build({lastInvite})).inviteLink() ?? '');

      expect(link.origin).toBe(ORIGIN);
      expect(link.pathname).toBe('/accept-invite');
      expect(link.searchParams.get('user')).toBe('u-1');
      expect(link.searchParams.get('token')).toBe('a+b/c=');
    });
  });

  describe('error messages', () => {
    it('are empty without an error', () => {
      const result = compute(build());
      expect(result.loadErrorMessage()).toBe('');
      expect(result.inviteErrorMessage()).toBe('');
      expect(result.revokeErrorMessage()).toBe('');
    });

    it('resolve known codes through the registry', () => {
      const result = compute(
        build({
          inviteStatus: 'error',
          inviteErrorCode: 'DUPLICATE_EMAIL',
          revokeErrorCode: 'CANNOT_REVOKE_OWNER',
        })
      );
      expect(result.inviteErrorMessage()).toBe('This email already belongs to an account.');
      expect(result.revokeErrorMessage()).toBe("The owner's access cannot be revoked.");
    });

    it('fall back to feature defaults for unknown codes', () => {
      const result = compute(
        build({
          status: 'error',
          errorCode: 'UNKNOWN',
          inviteStatus: 'error',
          inviteErrorCode: null,
          revokeErrorCode: 'UNKNOWN',
        })
      );
      expect(result.loadErrorMessage()).toBe('Failed to load people.');
      expect(result.inviteErrorMessage()).toBe('Could not create the invite.');
      expect(result.revokeErrorMessage()).toBe('Could not revoke access.');
    });
  });
});
