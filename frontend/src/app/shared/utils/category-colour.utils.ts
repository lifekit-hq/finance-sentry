import {
  CATEGORY_FALLBACK_STEPS,
  CATEGORY_HASH_MULTIPLIER,
  CATEGORY_SERIES_STEP,
} from '../constants/category-colour/category-colour.constants';
import {type ChartColour} from '../models/chart-colour/chart-colour.model';

export class CategoryColourUtils {
  /**
   * Series colour of a category, keyed by its identity: the same key gets the same step whatever
   * the range, rank or theme. An unmapped key hashes into the asset-clear steps.
   */
  public static of(category: string): ChartColour {
    const mapped = CATEGORY_SERIES_STEP[category];
    if (mapped !== undefined) {
      return {step: mapped};
    }
    let hash = 0;
    for (const char of category) {
      hash = (hash * CATEGORY_HASH_MULTIPLIER + char.charCodeAt(0)) >>> 0;
    }
    return {step: CATEGORY_FALLBACK_STEPS[hash % CATEGORY_FALLBACK_STEPS.length]};
  }
}
