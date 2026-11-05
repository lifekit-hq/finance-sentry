import {type Nullable} from '@lifekit-hq/ui';

import {
  DEFAULT_PRICE_SELECTION,
  MOVING_AVERAGE_IDS,
  PRICE_CHART_ANONYMOUS_KEY,
  PRICE_CHART_STORAGE_PREFIX,
  PRICE_KINDS,
} from '../constants/price-chart/price-chart.constants';
import {
  type MovingAverageId,
  type PriceChartKind,
  type PriceChartSelection,
} from '../models/price-chart/price-chart.model';

/**
 * The chart kind and the moving averages a person left on, kept per user on this device so "SMA 200 only"
 * is still on at the next visit. Storage can be blocked or hold something else, so every access is guarded
 * and anything unrecognised falls back to the default.
 */
export class PriceChartSelectionUtils {
  public static key(userId: Nullable<string>): string {
    return `${PRICE_CHART_STORAGE_PREFIX}${userId ?? PRICE_CHART_ANONYMOUS_KEY}`;
  }

  public static read(userId: Nullable<string>): PriceChartSelection {
    try {
      const raw = localStorage.getItem(PriceChartSelectionUtils.key(userId));
      return raw === null ? DEFAULT_PRICE_SELECTION : PriceChartSelectionUtils.parse(raw);
    } catch {
      return DEFAULT_PRICE_SELECTION;
    }
  }

  public static write(userId: Nullable<string>, selection: PriceChartSelection): void {
    try {
      localStorage.setItem(PriceChartSelectionUtils.key(userId), JSON.stringify(selection));
    } catch {
      // Storage blocked: the chart opens on its defaults next visit, nothing else depends on it.
    }
  }

  /** `current` with `id` flipped, in the catalogue's order so the stored list is stable. */
  public static toggle(
    current: readonly MovingAverageId[],
    id: MovingAverageId
  ): MovingAverageId[] {
    const next = new Set(current);
    if (!next.delete(id)) {
      next.add(id);
    }
    return MOVING_AVERAGE_IDS.filter(known => next.has(known));
  }

  private static parse(raw: string): PriceChartSelection {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null) {
      return DEFAULT_PRICE_SELECTION;
    }
    const {kind, movingAverages} = parsed as Record<string, unknown>;
    const kinds: readonly string[] = PRICE_KINDS.map(k => k.value);
    return {
      kind:
        typeof kind === 'string' && kinds.includes(kind)
          ? (kind as PriceChartKind)
          : DEFAULT_PRICE_SELECTION.kind,
      movingAverages: Array.isArray(movingAverages)
        ? MOVING_AVERAGE_IDS.filter(id => movingAverages.includes(id))
        : [],
    };
  }
}
