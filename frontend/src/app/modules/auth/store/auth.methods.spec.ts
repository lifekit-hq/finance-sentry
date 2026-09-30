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
  user: {id: 'user-123', email: 'user@test.com', roles: []},
  expiresAt: '2099-01-01T00:00:00Z',
};

describe('authMethods', () => {
  describe('applyAuthResponse', () => {
    it('patches state with userId/email and resets status/error/flow/flashMessage', () => {
      const {state, methods} = makeState();
      methods.setLoading('login');
      methods.setFlashMessage({kind: 'info', text: 'stale'});

      methods.applyAuthResponse(SAMPLE_RESPONSE);

      expect(state.userId()).toBe(SAMPLE_RESPONSE.user.id);
      expect(state.email()).toBe(SAMPLE_RESPONSE.user.email);
      expect(state.status()).toBe('idle');
      expect(state.errorCode()).toBeNull();
      expect(state.flow()).toBeNull();
      expect(state.flashMessage()).toBeNull();
    });
  });

  describe('clearSession', () => {
    it('clears userId and email but preserves flashMessage', () => {
      const {state, methods} = makeState();
      methods.applyAuthResponse(SAMPLE_RESPONSE);
      methods.setFlashMessage({kind: 'error', text: 'keep me'});

      methods.clearSession();

      expect(state.userId()).toBeNull();
      expect(state.email()).toBeNull();
      expect(state.status()).toBe('idle');
      expect(state.errorCode()).toBeNull();
      expect(state.flow()).toBeNull();
      expect(state.flashMessage()).toEqual({kind: 'error', text: 'keep me'});
    });
  });

  describe('setLoading', () => {
    it('sets status to loading with the given flow and clears errorCode', () => {
      const {state, methods} = makeState();
      methods.setError('SOMETHING', 'login');

      methods.setLoading('register');

      expect(state.status()).toBe('loading');
      expect(state.flow()).toBe('register');
      expect(state.errorCode()).toBeNull();
    });
  });

  describe('setError', () => {
    it('sets status to error with code and flow', () => {
      const {state, methods} = makeState();
      methods.setError('DUPLICATE_EMAIL', 'register');
      expect(state.status()).toBe('error');
      expect(state.errorCode()).toBe('DUPLICATE_EMAIL');
      expect(state.flow()).toBe('register');
    });
  });

  describe('resetError', () => {
    it('resets status to idle and clears errorCode', () => {
      const {state, methods} = makeState();
      methods.setError('X', 'login');
      methods.resetError();
      expect(state.status()).toBe('idle');
      expect(state.errorCode()).toBeNull();
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
