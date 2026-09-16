import {describe, expect, it} from 'vitest';

import {type Provider} from '../../models/provider/provider.model';
import {PROVIDER_CATALOG} from './providers.constants';

const ALL_SLUGS: readonly Provider[] = ['monobank', 'binance', 'revolut_x', 'ibkr', 'truelayer'];

describe('PROVIDER_CATALOG', () => {
  it('contains exactly one descriptor per Provider', () => {
    expect(PROVIDER_CATALOG).toHaveLength(ALL_SLUGS.length);
    for (const slug of ALL_SLUGS) {
      const matches = PROVIDER_CATALOG.filter(p => p.slug === slug);
      expect(matches).toHaveLength(1);
    }
  });

  it('exposes frozen descriptors', () => {
    expect(Object.isFrozen(PROVIDER_CATALOG)).toBe(true);
    for (const descriptor of PROVIDER_CATALOG) {
      expect(Object.isFrozen(descriptor)).toBe(true);
    }
  });

  it('uses /assets/providers/<slug>.svg icon paths', () => {
    for (const descriptor of PROVIDER_CATALOG) {
      expect(descriptor.iconAsset).toBe(`/assets/providers/${descriptor.slug}.svg`);
    }
  });

  it('maps providers to expected institution types', () => {
    const byType = new Map(PROVIDER_CATALOG.map(p => [p.slug, p.institutionType]));
    expect(byType.get('monobank')).toBe('bank');
    expect(byType.get('binance')).toBe('crypto');
    expect(byType.get('revolut_x')).toBe('crypto');
    expect(byType.get('ibkr')).toBe('broker');
    expect(byType.get('truelayer')).toBe('bank');
  });

  it('maps providers to expected form shapes', () => {
    const byShape = new Map(PROVIDER_CATALOG.map(p => [p.slug, p.formShape]));
    expect(byShape.get('monobank')).toBe('token');
    expect(byShape.get('binance')).toBe('key-secret');
    expect(byShape.get('revolut_x')).toBe('key-private-key');
    expect(byShape.get('ibkr')).toBe('user-pass');
    expect(byShape.get('truelayer')).toBe('open-banking-picker');
  });
});
