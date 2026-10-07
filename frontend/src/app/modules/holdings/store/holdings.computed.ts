import {computed, inject, type Signal} from '@angular/core';
import {SERIES, seriesColor} from '@lifekit-hq/charts-core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {type DonutSegment} from '@lifekit-hq/ui';

import {ChartColorUtils} from '../../../shared/utils/chart-color.utils';
import {ProviderUtils} from '../../../shared/utils/provider.utils';
import {CRYPTO_PROVIDERS} from '../constants/position/position.constants';
import {type Position} from '../models/position/position.model';
import {DayChangeUtils} from '../utils/day-change.utils';
import {type HoldingsState} from './holdings.state';

interface StateSignals {
  positions: Signal<Position[]>;
  positionsStatus: Signal<HoldingsState['positionsStatus']>;
  positionsErrorCode: Signal<Nullable<string>>;
  dayChangePctByTicker: Signal<Record<string, number>>;
}

const DEFAULT_POSITIONS_ERROR = 'Failed to load positions.';
const WEIGHT_TO_PERCENT = 100;

export type AssetClass = 'equity' | 'crypto' | 'venueCash';

export interface PositionRow {
  symbol: string;
  provider: string;
  providerLabel: string;
  quantity: number;
  currentPrice: Nullable<number>;
  currentValue: number;
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

const ASSET_CLASS_ORDER: readonly AssetClass[] = ['equity', 'crypto', 'venueCash'];

const ASSET_CLASS_LABEL: Record<AssetClass, string> = {
  equity: 'Equities',
  crypto: 'Crypto',
  venueCash: 'Venue cash',
};

// Chart-series steps; resolved when the donut is built, since a canvas cannot read a CSS var.
const ASSET_CLASS_SERIES: Record<AssetClass, number> = {
  equity: SERIES.accent,
  crypto: SERIES.amber,
  venueCash: SERIES.green,
};

function resolveAssetClass(position: Position): AssetClass {
  if (position.isVenueCash) {
    return 'venueCash';
  }
  return CRYPTO_PROVIDERS.has(position.provider) ? 'crypto' : 'equity';
}

export function holdingsComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  const totalPositionsValue = computed(() =>
    store.positions().reduce((sum, p) => sum + p.currentValue, 0)
  );

  const positionsByAssetClass = computed((): PositionAssetGroup[] => {
    const positions = store.positions();
    const totalValue = positions.reduce((sum, p) => sum + p.currentValue, 0);
    const dayChanges = store.dayChangePctByTicker();
    const groups = new Map<AssetClass, PositionRow[]>();

    for (const p of positions) {
      const assetClass = resolveAssetClass(p);
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
    isPositionsLoading: computed(() => store.positionsStatus() === 'loading'),
    positionsLoaded: computed(
      () => store.positionsStatus() !== 'idle' || store.positions().length > 0
    ),
    positionsErrorMessage: computed(() => {
      if (store.positionsStatus() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.positionsErrorCode()) ?? DEFAULT_POSITIONS_ERROR;
    }),
    totalPositionsValue,
    positionsByAssetClass,
    allocationSegments: computed((): DonutSegment[] =>
      positionsByAssetClass().map(group => ({
        label: group.label,
        value: group.totalValue,
        color: seriesColor(ASSET_CLASS_SERIES[group.assetClass]),
      }))
    ),
    allocationBreakdown: computed((): AllocationBreakdownRow[] => {
      const groups = positionsByAssetClass();
      const total = groups.reduce((sum, g) => sum + g.totalValue, 0);
      return groups.map(group => ({
        label: group.label,
        color: ChartColorUtils.series(ASSET_CLASS_SERIES[group.assetClass]),
        value: group.totalValue,
        percent: total > 0 ? (group.totalValue / total) * WEIGHT_TO_PERCENT : 0,
      }));
    }),
  };
}
