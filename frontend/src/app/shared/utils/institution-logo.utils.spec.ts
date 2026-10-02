import {InstitutionLogoUtils} from './institution-logo.utils';

describe('InstitutionLogoUtils', () => {
  describe('faviconUrl', () => {
    it('resolves a direct-connect provider code to its favicon URL', () => {
      expect(InstitutionLogoUtils.faviconUrl('binance', 'Binance')).toBe(
        'https://www.google.com/s2/favicons?domain=binance.com&sz=64'
      );
    });

    it('resolves IBKR and Monobank provider codes', () => {
      expect(InstitutionLogoUtils.faviconUrl('ibkr', null)).toContain('interactivebrokers.com');
      expect(InstitutionLogoUtils.faviconUrl('monobank', null)).toContain('monobank.ua');
    });

    it('is case-insensitive on the provider code', () => {
      expect(InstitutionLogoUtils.faviconUrl('BINANCE', null)).toContain('binance.com');
    });

    it('resolves an aggregator-connected bank by name when the provider is generic', () => {
      expect(InstitutionLogoUtils.faviconUrl('truelayer', 'Revolut')).toContain('revolut.com');
    });

    it('matches bank names by case-insensitive substring', () => {
      expect(InstitutionLogoUtils.faviconUrl('truelayer', 'Allied Irish Banks (GB)')).toContain(
        'aib.ie'
      );
    });

    it.each([
      ['Monobank', 'monobank.ua'],
      ['Interactive Brokers', 'interactivebrokers.com'],
      ['Binance', 'binance.com'],
    ])('resolves %s by name alone (no provider)', (name, domain) => {
      expect(InstitutionLogoUtils.faviconUrl(null, name)).toContain(domain);
    });

    it('prefers the provider code over the name when both match', () => {
      // provider binance wins even though the name mentions revolut
      expect(InstitutionLogoUtils.faviconUrl('binance', 'revolut')).toContain('binance.com');
    });

    it('returns null for an unknown institution', () => {
      expect(InstitutionLogoUtils.faviconUrl('truelayer', 'Some Local Credit Union')).toBeNull();
    });

    it('returns null when both provider and name are null', () => {
      expect(InstitutionLogoUtils.faviconUrl(null, null)).toBeNull();
    });

    it('returns null when both provider and name are blank', () => {
      expect(InstitutionLogoUtils.faviconUrl('   ', '  ')).toBeNull();
    });
  });
});
