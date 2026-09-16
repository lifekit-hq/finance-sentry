import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {type DonutSegment} from '@lifekit-hq/ui';

import {type Position} from '../models/position/position.model';
import {type HoldingsState} from './holdings.state';

interface StateSignals {
  positions: Signal<Position[]>;
  positionsStatus: Signal<HoldingsState['positionsStatus']>;
  positionsErrorCode: Signal<Nullable<string>>;
}

const DEFAULT_POSITIONS_ERROR = 'Failed to load positions.';
const WEIGHT_TO_PERCENT = 100;

export type AssetClass = 'equity' | 'crypto' | 'venueCash';

export interface PositionRow {
  symbol: string;
  provider: string;
  quantity: number;
  currentPrice: Nullable<number>;
  currentValue: number;
  pnlPercent: Nullable<number>;
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

const ASSET_CLASS_COLOR: Record<AssetClass, string> = {
  equity: '#6366f1',
  crypto: '#f59e0b',
  venueCash: '#10b981',
};

const CRYPTO_PROVIDERS = new Set<string>(['binance', 'revolut_x']);

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
    const groups = new Map<AssetClass, PositionRow[]>();

    for (const p of positions) {
      const assetClass = resolveAssetClass(p);
      const row: PositionRow = {
        symbol: p.symbol,
        provider: p.provider,
        quantity: p.quantity,
        currentPrice: p.currentPrice,
        currentValue: p.currentValue,
        pnlPercent: p.pnlPercent,
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
        color: ASSET_CLASS_COLOR[group.assetClass],
      }))
    ),
    allocationBreakdown: computed((): AllocationBreakdownRow[] => {
      const groups = positionsByAssetClass();
      const total = groups.reduce((sum, g) => sum + g.totalValue, 0);
      return groups.map(group => ({
        label: group.label,
        color: ASSET_CLASS_COLOR[group.assetClass],
        value: group.totalValue,
        percent: total > 0 ? (group.totalValue / total) * WEIGHT_TO_PERCENT : 0,
      }));
    }),
  };
}
