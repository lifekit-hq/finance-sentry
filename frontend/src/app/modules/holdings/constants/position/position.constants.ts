/** Venues whose non-fiat holdings are crypto assets. */
export const CRYPTO_PROVIDERS: ReadonlySet<string> = new Set<string>(['binance', 'revolut_x']);

/** Brokerage instrument type of a listed stock - the only rows a research quote can price. */
export const EQUITY_INSTRUMENT_TYPE = 'STK';
