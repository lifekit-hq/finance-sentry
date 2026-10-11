import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type AuthResponse} from '../models/auth/auth.model';
import {authMethods} from './auth.methods';
import {initialAuthState} from './auth.state';

function makeState() {
  const state = signalState(initialAuthState);
  return {state, methods: authMethods(state)};
}

const SAMPLE_RESPONSE: AuthResponse = {
  user: {
    id: 'user-123',
    email: 'user@test.com',
    roles: ['Member'],
    permissions: ['connections.manage'],
  },
  expiresAt: '2099-01-01T00:00:00Z',
};

describe('authMethods', () => {
  describe('applyAuthResponse', () => {
    it('patches state with userId/email/roles/permissions and clears the flashMessage', () => {
      const {state, methods} = makeState();
      methods.setFlashMessage({kind: 'info', text: 'stale'});

      methods.applyAuthResponse(SAMPLE_RESPONSE);

      expect(state.userId()).toBe(SAMPLE_RESPONSE.user.id);
      expect(state.email()).toBe(SAMPLE_RESPONSE.user.email);
      expect(state.roles()).toEqual(['Member']);
      expect(state.permissions()).toEqual(['connections.manage']);
      expect(state.flashMessage()).toBeNull();
    });
  });

  describe('clearSession', () => {
    it('clears userId, email, roles and permissions but preserves flashMessage', () => {
      const {state, methods} = makeState();
      methods.applyAuthResponse(SAMPLE_RESPONSE);
      methods.setFlashMessage({kind: 'error', text: 'keep me'});

      methods.clearSession();

      expect(state.userId()).toBeNull();
      expect(state.email()).toBeNull();
      expect(state.roles()).toEqual([]);
      expect(state.permissions()).toEqual([]);
      expect(state.flashMessage()).toEqual({kind: 'error', text: 'keep me'});
    });
  });

  describe('setReturnUrl / setFlashMessage', () => {
    it('updates returnUrl', () => {
      const {state, methods} = makeState();
      methods.setReturnUrl('/dashboard');
      expect(state.returnUrl()).toBe('/dashboard');
    });

    it('updates flashMessage', () => {
      const {state, methods} = makeState();
      const msg = {kind: 'info', text: 'hi'} as const;
      methods.setFlashMessage(msg);
      expect(state.flashMessage()).toEqual(msg);
    });
  });
});
