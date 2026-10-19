const PERCENT = 100;
const MAX_PERCENT_DIGITS = 2;

export class PercentUtils {
  /** A fraction as the percent number a person types and reads: 0.04 → "4", 0.035 → "3.5". */
  public static fromFraction(fraction: number): string {
    return Number((fraction * PERCENT).toFixed(MAX_PERCENT_DIGITS)).toString();
  }

  /**
   * Percentage points with their sign, to `digits` places: 4.21 → "+4.2%", -3 → "-3.0%". A figure that
   * rounds to zero reads "0.0%", never "-0.0%" (finance-framework D2).
   */
  public static signed(points: number, digits: number): string {
    const fixed = points.toFixed(digits);
    if (Number(fixed) === 0) {
      return `${(0).toFixed(digits)}%`;
    }
    return `${points > 0 ? '+' : ''}${fixed}%`;
  }

  /**
   * The fraction behind a typed percent, or null when the text is not a number or falls outside
   * `[minPercent, maxPercent]` — the caller keeps its previous value rather than saving nonsense.
   */
  public static toFraction(text: string, minPercent: number, maxPercent: number): Nullable<number> {
    const trimmed = text.trim();
    if (trimmed === '') {
      return null;
    }
    const percent = Number(trimmed);
    if (!Number.isFinite(percent) || percent < minPercent || percent > maxPercent) {
      return null;
    }
    return percent / PERCENT;
  }
}
