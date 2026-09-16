const COINCAP_ICON_BASE = 'https://assets.coincap.io/assets/icons';
const FMP_STOCK_IMAGE_BASE = 'https://financialmodelingprep.com/image-stock';

/**
 * Builds a public logo URL for a held asset without hosting any images:
 * - crypto (Binance, Revolut X) → CoinCap coin icon by symbol, e.g. sol@2x.png
 * - stock/ETF (IBKR) → Financial Modeling Prep image-stock by ticker, e.g. AAPL.png
 *
 * Both sources are keyless. Returns null for anything unrecognised — callers
 * fall back to the initials avatar.
 */
export class AssetLogoUtils {
  public static logoUrl(symbol: Nullable<string>, provider: Nullable<string>): Nullable<string> {
    const sym = symbol?.trim();
    if (!sym) {
      return null;
    }

    switch (provider?.trim().toLowerCase()) {
      case 'binance':
      case 'revolut_x':
        return `${COINCAP_ICON_BASE}/${sym.toLowerCase()}@2x.png`;
      case 'ibkr':
        return `${FMP_STOCK_IMAGE_BASE}/${sym.toUpperCase()}.png`;
      default:
        return null;
    }
  }
}
