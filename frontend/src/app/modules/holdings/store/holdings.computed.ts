import {computed, type Signal} from '@angular/core';
import {CATEGORICAL_STEPS} from '@lifekit-hq/charts-core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type DonutSegment} from '@lifekit-hq/ui';

import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
} from '../../../shared/constants/chart-colour/chart-colour.constants';
import {STALE_SOURCE_CODE} from '../../../shared/constants/headline/headline.constants';
import {PROVIDER_CATALOG} from '../../../shared/constants/providers/providers.constants';
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
  provider: Signal<Nullable<string>>;
}

const WEIGHT_TO_PERCENT = 100;

/** How many positions the Home Investments card lists. */
export const TOP_HOLDINGS_COUNT = 5;

// The headline names a failed read by feed ('brokerage' | 'crypto'), not by provider.
const CRYPTO_FEED = 'crypto';
const BROKERAGE_FEED = 'brokerage';

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

export interface ProviderOption {
  slug: string;
  label: string;
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
  // The provider filter narrows everything the page shows - total, weights, groups and donut.
  const visiblePositions = computed((): Position[] => {
    const provider = store.provider();
    return provider ? store.positions().filter(p => p.provider === provider) : store.positions();
  });
  const isProviderFeedFailed = computed(() => {
    const provider = store.provider();
    const institutionType = PROVIDER_CATALOG.find(p => p.slug === provider)?.institutionType;
    if (!institutionType) {
      return false;
    }
    const feed = institutionType === 'crypto' ? CRYPTO_FEED : BROKERAGE_FEED;
    return (store.headline()?.reasons ?? []).some(
      r => r.code === STALE_SOURCE_CODE && r.source === feed
    );
  });
  const totalPositionsValue = computed(() =>
    visiblePositions().reduce((sum, p) => sum + p.currentValue, 0)
  );
  // Everything but the cash asset class (broker cash, venue fiat): the book's `investedUsd`
  // (money-semantics §8), so Home's card and its hero sub-line state one figure.
  const investedPositionsValue = computed(() =>
    visiblePositions()
      .filter(p => p.assetClass !== 'cash')
      .reduce((sum, p) => sum + p.currentValue, 0)
  );

  const positionsByAssetClass = computed((): PositionAssetGroup[] => {
    const positions = visiblePositions();
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
    positions: visiblePositions,
    providerFilter: computed(() => store.provider()),
    providerFilterLabel: computed(() => {
      const provider = store.provider();
      return provider ? ProviderUtils.label(provider) : null;
    }),
    // A filter with no matching rows is its own state, never the "connect an account" empty page.
    isFilteredEmpty: computed(
      () =>
        store.provider() !== null &&
        visiblePositions().length === 0 &&
        store.positions().length > 0 &&
        !isProviderFeedFailed()
    ),
    // The filtered provider's feed failed to load: its empty list is not an empty provider.
    isProviderFeedFailed,
    positionsLoaded: computed(() => store.status() !== 'idle' || store.positions().length > 0),
    totalPositionsValue,
    investedPositionsValue,
    // The total's record: its badge, and N/A (never 0) when every feed failed.
    totalHeadline: computed((): Nullable<Headline> => store.headline()),
    isTotalUnavailable: computed(
      () => isProviderFeedFailed() || HeadlineUtils.isUnavailable(store.headline())
    ),
    // A failed feed is never read as an empty book: the total card keeps its N/A and badge.
    isEmpty: computed(
      () => store.positions().length === 0 && HeadlineUtils.badge(store.headline()) === null
    ),
    positionsByAssetClass,
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
