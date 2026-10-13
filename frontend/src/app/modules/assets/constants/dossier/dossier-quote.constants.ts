/** Canonical asset class of a holding a research quote can price. */
export const EQUITY_ASSET_CLASS = 'Equities';

/** Minor units Yahoo quotes some exchanges in (LSE pence, Johannesburg cents, Tel Aviv agorot) and their major currency. */
export const MINOR_UNIT_CURRENCIES: ReadonlyMap<string, string> = new Map([
  ['GBp', 'GBP'],
  ['GBX', 'GBP'],
  ['ZAc', 'ZAR'],
  ['ILA', 'ILS'],
]);

export const MINOR_UNITS_PER_MAJOR = 100;
