const DEFAULT_CURRENCY = 'USD';
const DEFAULT_FRACTION_DIGITS = 2;
const EN_US = 'en-US';
const EMPTY_VALUE = '—';

// Symbol-first only where the symbol identifies the currency on its own; "¥" (JPY/CNY),
// "kr" (SEK/NOK/DKK) and the many "$" currencies are ambiguous, so those fall back to the ISO code.
const UNAMBIGUOUS_SYMBOLS: Readonly<Record<string, string>> = {
  USD: '$',
  EUR: '€',
  GBP: '£',
  UAH: '₴',
};

export interface MoneyFormatOptions {
  /** Defaults to 2, or to `maxFractionDigits` when that is lower. */
  minFractionDigits?: number;
  /** Defaults to 2. */
  maxFractionDigits?: number;
  /** Prefix positive amounts with "+" (negative amounts always carry "-"). */
  signed?: boolean;
}

export class MoneyUtils {
  /**
   * "$18,981.48", "-€1,200.00", "CHF 950.00". Unknown or missing amounts render as an em dash.
   */
  public static format(
    value: number | null | undefined,
    currency: string | null | undefined = DEFAULT_CURRENCY,
    options: MoneyFormatOptions = {}
  ): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return EMPTY_VALUE;
    }
    const maxFractionDigits = options.maxFractionDigits ?? DEFAULT_FRACTION_DIGITS;
    const minFractionDigits =
      options.minFractionDigits ?? Math.min(DEFAULT_FRACTION_DIGITS, maxFractionDigits);
    const magnitude = new Intl.NumberFormat(EN_US, {
      minimumFractionDigits: minFractionDigits,
      maximumFractionDigits: maxFractionDigits,
    }).format(Math.abs(value));

    const code = (currency || DEFAULT_CURRENCY).toUpperCase();
    const symbol = UNAMBIGUOUS_SYMBOLS[code];
    const body = symbol ? `${symbol}${magnitude}` : `${code} ${magnitude}`;

    // A value that rounds to zero must not read as "-$0.00".
    const isNegative = value < 0 && Number(magnitude.replaceAll(',', '')) !== 0;
    if (isNegative) {
      return `-${body}`;
    }
    return options.signed && value > 0 ? `+${body}` : body;
  }

  /** The muted second line shown under a native amount: "~ $1,234". Whole units, no cents. */
  public static formatEquivalent(value: number, currency: string = DEFAULT_CURRENCY): string {
    return `~ ${MoneyUtils.format(value, currency, {maxFractionDigits: 0})}`;
  }
}
