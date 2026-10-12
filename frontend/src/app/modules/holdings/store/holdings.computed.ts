import {computed, type Signal} from '@angular/core';
import {CATEGORICAL_STEPS} from '@lifekit-hq/charts-core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type DonutSegment} from '@lifekit-hq/ui';

import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
} from '../../../shared/constants/chart-colour/chart-colour.constants';
import {type ChartColour} from '../../../shared/models/chart-colour/chart-colour.model';
import {ChartColorUtils} from '../../../shared/utils/chart-color.utils';
import {ProviderUtils} from '../../../shared/utils/provider.utils';
import {type Position, type PositionAssetClass} from '../models/position/position.model';
import {DayChangeUtils} from '../utils/day-change.utils';

interface StateSignals {
  positions: Signal<Position[]>;
  status: Signal<AsyncStatus>;
  dayChangePctByTicker: Signal<Record<string, number>>;
}

const WEIGHT_TO_PERCENT = 100;

export type AssetClass = PositionAssetClass;

export interface PositionRow {
  symbol: string;
  provider: string;
  providerLabel: string;
  quantity: number;
  currentPrice: Nullable<number>;
  currentValue: number;
  /** Set when the provider denominates the row outside USD: its amounts are shown in that currency. */
  nativeCurrency?: Nullable<string>;
  nativeValue?: Nullable<number>;
  nativePrice?: Nullable<number>;
  pnlPercent: Nullable<number>;
  pnlUsd: Nullable<number>;
  /** Today's move from the research quote; null when the row has no quote. */
  dayChangePct: Nullable<number>;
  dayChangeUsd: Nullable<number>;
  weightPercent: number;
}

export interface PositionAssetGroup {
  assetClass: AssetClass;
  label: string;
  rows: PositionRow[];
  totalValue: number;
}

export interface AllocationBreakdownRow {
  label: string;
  color: string;
  value: number;
  percent: number;
}

const ASSET_CLASS_ORDER: readonly AssetClass[] = [
  'cash',
  'bonds',
  'realEstate',
  'equity',
  'crypto',
];

const ASSET_CLASS_LABEL: Record<AssetClass, string> = {
  cash: 'Cash',
  bonds: 'Bonds',
  realEstate: 'Real estate',
  equity: 'Equities',
  crypto: 'Crypto',
};

// Equity, crypto and cash wear their fixed asset tokens (never the theme); bonds and real estate
// have no fixed meaning and take series steps. A canvas cannot read a CSS var, so the donut resolves
// the colour when it is built.
export const ASSET_CLASS_COLOUR: Record<AssetClass, ChartColour> = {
  cash: CASH_COLOUR,
  bonds: {step: CATEGORICAL_STEPS[3]},
  realEstate: {step: CATEGORICAL_STEPS[4]},
  equity: EQUITY_COLOUR,
  crypto: CRYPTO_COLOUR,
};

export function holdingsComputed(store: StateSignals) {
  const totalPositionsValue = computed(() =>
    store.positions().reduce((sum, p) => sum + p.currentValue, 0)
  );

  const positionsByAssetClass = computed((): PositionAssetGroup[] => {
    const positions = store.positions();
    const totalValue = positions.reduce((sum, p) => sum + p.currentValue, 0);
    const dayChanges = store.dayChangePctByTicker();
    const groups = new Map<AssetClass, PositionRow[]>();

    for (const p of positions) {
      const assetClass = p.assetClass;
      const dayChangePct = DayChangeUtils.isQuotable(p)
        ? (dayChanges[p.symbol.toUpperCase()] ?? null)
        : null;
      const row: PositionRow = {
        symbol: p.symbol,
        provider: p.provider,
        providerLabel: ProviderUtils.label(p.provider),
        quantity: p.quantity,
        currentPrice: p.currentPrice,
        currentValue: p.currentValue,
        nativeCurrency: p.nativeCurrency ?? null,
        nativeValue: p.nativeValue ?? null,
        nativePrice: p.nativeValue != null && p.quantity > 0 ? p.nativeValue / p.quantity : null,
        pnlPercent: p.pnlPercent,
        pnlUsd: p.pnlUsd,
        dayChangePct,
        dayChangeUsd: DayChangeUtils.dayChangeUsd(p.currentValue, dayChangePct),
        weightPercent: totalValue > 0 ? (p.currentValue / totalValue) * WEIGHT_TO_PERCENT : 0,
      };
      const bucket = groups.get(assetClass);
      if (bucket) {
        bucket.push(row);
      } else {
        groups.set(assetClass, [row]);
      }
    }

    return ASSET_CLASS_ORDER.filter(cls => groups.has(cls)).map(cls => ({
      assetClass: cls,
      label: ASSET_CLASS_LABEL[cls],
      rows: (groups.get(cls) ?? []).sort((a, b) => b.currentValue - a.currentValue),
      totalValue: (groups.get(cls) ?? []).reduce((sum, r) => sum + r.currentValue, 0),
    }));
  });

  return {
    positions: computed((): Position[] => store.positions()),
    positionsLoaded: computed(() => store.status() !== 'idle' || store.positions().length > 0),
    totalPositionsValue,
    positionsByAssetClass,
    allocationSegments: computed((): DonutSegment[] =>
      positionsByAssetClass().map(group => ({
        label: group.label,
        value: group.totalValue,
        color: ChartColorUtils.canvas(ASSET_CLASS_COLOUR[group.assetClass]),
      }))
    ),
    allocationBreakdown: computed((): AllocationBreakdownRow[] => {
      const groups = positionsByAssetClass();
      const total = groups.reduce((sum, g) => sum + g.totalValue, 0);
      return groups.map(group => ({
        label: group.label,
        color: ChartColorUtils.css(ASSET_CLASS_COLOUR[group.assetClass]),
        value: group.totalValue,
        percent: total > 0 ? (group.totalValue / total) * WEIGHT_TO_PERCENT : 0,
      }));
    }),
  };
}
