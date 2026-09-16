import {AssetLogoUtils} from './asset-logo.utils';

describe('AssetLogoUtils', () => {
  describe('logoUrl', () => {
    it('builds a CoinCap icon URL for a Binance crypto asset (lowercased)', () => {
      expect(AssetLogoUtils.logoUrl('SOL', 'binance')).toBe(
        'https://assets.coincap.io/assets/icons/sol@2x.png'
      );
    });

    it('builds the same CoinCap icon URL for a Revolut X crypto asset', () => {
      expect(AssetLogoUtils.logoUrl('BTC', 'revolut_x')).toBe(
        'https://assets.coincap.io/assets/icons/btc@2x.png'
      );
    });

    it('builds an FMP image-stock URL for an IBKR ticker (uppercased)', () => {
      expect(AssetLogoUtils.logoUrl('aapl', 'ibkr')).toBe(
        'https://financialmodelingprep.com/image-stock/AAPL.png'
      );
    });

    it('is provider-case-insensitive', () => {
      expect(AssetLogoUtils.logoUrl('XRP', 'Binance')).toContain('/xrp@2x.png');
      expect(AssetLogoUtils.logoUrl('NVDA', 'IBKR')).toContain('/NVDA.png');
    });

    it('returns null for an unknown provider', () => {
      expect(AssetLogoUtils.logoUrl('AAPL', 'monobank')).toBeNull();
    });

    it('returns null for a blank symbol', () => {
      expect(AssetLogoUtils.logoUrl('  ', 'binance')).toBeNull();
    });

    it('returns null for a null symbol', () => {
      expect(AssetLogoUtils.logoUrl(null, 'binance')).toBeNull();
    });
  });
});
