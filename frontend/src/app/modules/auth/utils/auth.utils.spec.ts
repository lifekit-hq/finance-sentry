import {describe, expect, it} from 'vitest';

import {AuthUtils} from './auth.utils';

describe('AuthUtils.oidcStartUrl', () => {
  it('points at the API start endpoint', () => {
    expect(AuthUtils.oidcStartUrl('/api/v1', null)).toBe('/api/v1/auth/oidc/start');
  });

  it('carries the return path, encoded', () => {
    expect(AuthUtils.oidcStartUrl('/api/v1', '/budgets?month=2026-10')).toBe(
      '/api/v1/auth/oidc/start?returnUrl=%2Fbudgets%3Fmonth%3D2026-10'
    );
  });

  it('ignores an empty return path', () => {
    expect(AuthUtils.oidcStartUrl('/api/v1', '')).toBe('/api/v1/auth/oidc/start');
  });
});
