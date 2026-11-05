import {
  DISPOSAL_TAX_REASON_FALLBACK,
  DISPOSAL_TAX_REASON_LABEL,
  PERCENT_DECIMALS,
  PERCENT_SCALE,
} from '../constants/dossier/disposal-tax.constants';

export class DisposalTaxUtils {
  /** The sentence for a withheld reason code; unknown codes get the generic one, never a blank. */
  public static reasonText(code: string): string {
    return DISPOSAL_TAX_REASON_LABEL[code] ?? DISPOSAL_TAX_REASON_FALLBACK;
  }

  /** A rate fraction as a whole-or-decimal percent (`0.33` -> `33`), or null. */
  public static ratePercent(rate: number | null | undefined): number | null {
    return rate === null || rate === undefined
      ? null
      : Number((rate * PERCENT_SCALE).toFixed(PERCENT_DECIMALS));
  }
}
