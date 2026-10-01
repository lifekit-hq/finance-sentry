import {describe, expect, it} from 'vitest';

import {ProviderUtils} from './provider.utils';

describe('ProviderUtils.label', () => {
  it('returns the catalog display name for a known slug', () => {
    expect(ProviderUtils.label('monobank')).toBe('Monobank');
  });

  it('falls back to the slug for an unknown provider', () => {
    expect(ProviderUtils.label('mystery')).toBe('mystery');
  });
});
