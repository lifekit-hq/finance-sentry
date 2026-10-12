import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus, ERROR_MESSAGES} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {AuthStore} from '../../../auth/store/auth.store';
import {type Person} from '../../models/person/person.model';
import {peopleComputed} from './people.computed';

const CURRENT_USER_ID = 'owner-1';

function build(
  overrides: Partial<{
    people: Person[];
    peopleStatus: AsyncStatus;
    revokeErrorCode: Nullable<string>;
  }> = {}
) {
  return {
    people: signal<Person[]>(overrides.people ?? []),
    peopleStatus: signal<AsyncStatus>(overrides.peopleStatus ?? 'idle'),
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
      ],
    });
  });

  describe('isLoading', () => {
    it('is true while the first load runs', () => {
      expect(compute(build({peopleStatus: 'loading'})).isLoading()).toBe(true);
    });

    it('is false on a reload that already has people to show', () => {
      const people: Person[] = [{id: 'u', email: 'a@test.com', roles: [], status: 'Active'}];
      expect(compute(build({peopleStatus: 'loading', people})).isLoading()).toBe(false);
    });
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

  describe('revokeErrorMessage', () => {
    it('is empty without an error', () => {
      expect(compute(build()).revokeErrorMessage()).toBe('');
    });

    it('resolves a known code through the registry', () => {
      const result = compute(build({revokeErrorCode: 'CANNOT_REVOKE_OWNER'}));
      expect(result.revokeErrorMessage()).toBe("The owner's access cannot be revoked.");
    });

    it('falls back to the feature default for an unknown code', () => {
      const result = compute(build({revokeErrorCode: 'UNKNOWN'}));
      expect(result.revokeErrorMessage()).toBe('Could not revoke access.');
    });
  });
});
