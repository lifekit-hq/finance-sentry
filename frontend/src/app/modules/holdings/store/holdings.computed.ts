import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {type HiddenReason} from '../../../shared/models/wealth/wealth.model';
import {HeadlineUtils} from '../../../shared/utils/headline.utils';
import {ProviderUtils} from '../../../shared/utils/provider.utils';
import {type Position, type PositionAssetClass} from '../models/position/position.model';
import {DayChangeUtils} from '../utils/day-change.utils';

interface StateSignals {
  positions: Signal<Position[]>;
  headline: Signal<Nullable<Headline>>;
  status: Signal<AsyncStatus>;
  dayChangePctByTicker: Signal<Record<string, number>>;
}

const WEIGHT_TO_PERCENT = 100;

export type AssetClass = PositionAssetClass;

export interface PositionRow {
  symbol: string;
  provider: string;
  providerLabel: string;
  assetClass: AssetClass;
  quantity: number;
  currentPrice: Nullable<number>;
  /** Average purchase price per unit (USD); null when the provider gives no cost. */
  averagePrice: Nullable<number>;
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
  /** Set by the API when the list tucks the row away; it still counts in totals and weights. */
  hiddenReason: Nullable<HiddenReason>;
  weightPercent: number;
}

export interface PositionAssetGroup {
  assetClass: AssetClass;
  label: string;
  /** Rows the list shows. */
  rows: PositionRow[];
  /** Rows the API flagged hidden, closed behind one "N hidden" row; empty when nothing is. */
  hiddenRows: PositionRow[];
  /** Hidden rows' summed value; already inside `totalValue`. */
  hiddenTotal: number;
  /** The whole class: visible and hidden rows. */
  totalValue: number;
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

export function holdingsComputed(store: StateSignals) {
  const totalPositionsValue = computed(() =>
    store.positions().reduce((sum, p) => sum + p.currentValue, 0)
  );
  // Everything but the cash asset class (broker cash, venue fiat): the book's `investedUsd`
  // (money-semantics §8), so Home's card and its hero sub-line state one figure.
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
        assetClass,
        quantity: p.quantity,
        currentPrice: p.currentPrice,
        averagePrice: p.averagePrice,
        currentValue: p.currentValue,
        nativeCurrency: p.nativeCurrency ?? null,
        nativeValue: p.nativeValue ?? null,
        nativePrice: p.nativeValue != null && p.quantity > 0 ? p.nativeValue / p.quantity : null,
        pnlPercent: p.pnlPercent,
        pnlUsd: p.pnlUsd,
        dayChangePct,
        dayChangeUsd: DayChangeUtils.dayChangeUsd(p.currentValue, dayChangePct),
        hiddenReason: p.hiddenReason ?? null,
        weightPercent: totalValue > 0 ? (p.currentValue / totalValue) * WEIGHT_TO_PERCENT : 0,
      };
      const bucket = groups.get(assetClass);
      if (bucket) {
        bucket.push(row);
      } else {
        groups.set(assetClass, [row]);
      }
    }

    return ASSET_CLASS_ORDER.filter(cls => groups.has(cls)).map(cls => {
      const sorted = (groups.get(cls) ?? []).sort((a, b) => b.currentValue - a.currentValue);
      const hiddenRows = sorted.filter(r => r.hiddenReason !== null);
      return {
        assetClass: cls,
        label: ASSET_CLASS_LABEL[cls],
        rows: sorted.filter(r => r.hiddenReason === null),
        hiddenRows,
        hiddenTotal: hiddenRows.reduce((sum, r) => sum + r.currentValue, 0),
        totalValue: sorted.reduce((sum, r) => sum + r.currentValue, 0),
      };
    });
  });

  return {
    positions: store.positions,
    positionsLoaded: computed(() => store.status() !== 'idle' || store.positions().length > 0),
    totalPositionsValue,
    // A failed feed is never read as an empty book: the total card keeps its N/A and badge.
    isEmpty: computed(
      () => store.positions().length === 0 && HeadlineUtils.badge(store.headline()) === null
    ),
    positionsByAssetClass,
    // Every position of one provider, largest first, hidden rows included (they carry `hiddenReason`):
    // the Accounts accordion of that institution lists these.
    positionsByProvider: computed((): Record<string, PositionRow[]> => {
      const byProvider: Record<string, PositionRow[]> = {};
      for (const group of positionsByAssetClass()) {
        for (const row of [...group.rows, ...group.hiddenRows]) {
          (byProvider[row.provider] ??= []).push(row);
        }
      }
      for (const rows of Object.values(byProvider)) {
        rows.sort((a, b) => b.currentValue - a.currentValue);
      }
      return byProvider;
    }),
  };
}
