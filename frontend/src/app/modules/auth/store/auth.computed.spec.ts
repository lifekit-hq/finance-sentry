import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it} from 'vitest';

import {authComputed} from './auth.computed';

function build(
  overrides: Partial<{
    userId: Nullable<string>;
    permissions: string[];
    email: Nullable<string>;
    firstName: Nullable<string>;
    lastName: Nullable<string>;
  }> = {}
) {
  return {
    userId: signal<Nullable<string>>(overrides.userId ?? null),
    permissions: signal<string[]>(overrides.permissions ?? []),
    email: signal<Nullable<string>>(overrides.email ?? null),
    firstName: signal<Nullable<string>>(overrides.firstName ?? null),
    lastName: signal<Nullable<string>>(overrides.lastName ?? null),
  };
}

describe('authComputed', () => {
  describe('fullName', () => {
    it.each([
      ['both names', 'Ada', 'Lovelace', 'Ada Lovelace'],
      ['first name only', 'Ada', null, 'Ada'],
      ['last name only', null, 'Lovelace', 'Lovelace'],
      ['no names', null, null, ''],
    ])('joins %s', (_label, firstName, lastName, expected) => {
      const store = build({firstName, lastName});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).fullName()).toBe(expected);
      });
    });
  });

  describe('isAuthenticated', () => {
    it('returns true when userId is set', () => {
      const store = build({userId: 'u-1'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isAuthenticated()).toBe(true);
      });
    });

    it('returns false when userId is null', () => {
      const store = build({userId: null});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isAuthenticated()).toBe(false);
      });
    });
  });

  describe('canUseAi', () => {
    it('is true when the ai.use permission is held', () => {
      const store = build({permissions: ['connections.manage', 'ai.use']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canUseAi()).toBe(true);
      });
    });

    it('is false without the ai.use permission', () => {
      const store = build({permissions: ['connections.manage']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canUseAi()).toBe(false);
      });
    });
  });

  describe('canManageUsers', () => {
    it('is true when the users.manage permission is held', () => {
      const store = build({permissions: ['users.manage']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canManageUsers()).toBe(true);
      });
    });

    it('is false without the users.manage permission', () => {
      const store = build({permissions: ['ai.use']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canManageUsers()).toBe(false);
      });
    });
  });

  describe('avatarInitials', () => {
    it('uses first and last name initials when the profile name is known', () => {
      const store = build({email: 'test@gmail.com', firstName: 'Denys', lastName: 'Test'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).avatarInitials()).toBe('DT');
      });
    });

    it('falls back to the email first letter without a profile name', () => {
      const store = build({email: 'test@gmail.com'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).avatarInitials()).toBe('T');
      });
    });
  });
});
