const PERCENT = 100;
const MAX_PERCENT_DIGITS = 2;

export class PercentUtils {
  /** A fraction as the percent number a person types and reads: 0.04 → "4", 0.035 → "3.5". */
  public static fromFraction(fraction: number): string {
    return Number((fraction * PERCENT).toFixed(MAX_PERCENT_DIGITS)).toString();
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
