import {computed, type Signal} from '@angular/core';
import {CATEGORICAL_STEPS} from '@lifekit-hq/charts-core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type DonutSegment, type Nullable} from '@lifekit-hq/ui';

import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
} from '../../../shared/constants/chart-colour/chart-colour.constants';
import {type ChartColour} from '../../../shared/models/chart-colour/chart-colour.model';
import {type Headline} from '../../../shared/models/headline/headline.model';
import {type HiddenReason} from '../../../shared/models/wealth/wealth.model';
import {ChartColorUtils} from '../../../shared/utils/chart-color.utils';
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

/** How many positions the Home Investments card lists. */
export const TOP_HOLDINGS_COUNT = 5;

export type AssetClass = PositionAssetClass;

export interface PositionRow {
  symbol: string;
  provider: string;
  providerLabel: string;
  assetClass: AssetClass;
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
  // Everything but the cash asset class (broker cash, venue fiat): the book's `investedUsd`
  // (money-semantics §8), so Home's card and its hero sub-line state one figure.
  const investedPositionsValue = computed(() =>
    store
      .positions()
      .filter(p => p.assetClass !== 'cash')
      .reduce((sum, p) => sum + p.currentValue, 0)
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
        assetClass,
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
    investedPositionsValue,
    // The total's record: its badge, and N/A (never 0) when every feed failed.
    totalHeadline: computed((): Nullable<Headline> => store.headline()),
    isTotalUnavailable: computed(() => HeadlineUtils.isUnavailable(store.headline())),
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
    // Largest positions across every venue, for the Home card: cash balances have no dossier and
    // hidden rows are tucked away, so neither is listed (both still count in the weights).
    topHoldings: computed((): PositionRow[] =>
      positionsByAssetClass()
        .filter(group => group.assetClass !== 'cash')
        .flatMap(group => group.rows)
        .sort((a, b) => b.currentValue - a.currentValue)
        .slice(0, TOP_HOLDINGS_COUNT)
    ),
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
