import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {DateUtils} from '../../../../shared/utils/date.utils';
import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {
  type FlowBreakdown,
  type FlowBreakdownItem,
  type FlowBreakdownRange,
  type FlowBreakdownRequest,
  type FlowBucket,
} from '../../models/flow-breakdown/flow-breakdown.model';
import {MonthKeyUtils} from '../../utils/month-key.utils';

interface StateSignals {
  breakdown: Signal<Nullable<FlowBreakdown>>;
  selectedMonth: Signal<string>;
  from: Signal<Nullable<string>>;
  to: Signal<Nullable<string>>;
  months: Signal<number>;
  accountFilter: Signal<Nullable<string>>;
  status: Signal<AsyncStatus>;
}

/** One rendered section of the audit view: a bucket, its rows, and their USD sum. */
export interface BucketGroup {
  bucket: FlowBucket;
  label: string;
  counted: boolean;
  note: string;
  items: FlowBreakdownItem[];
  totalUsd: number;
  /** Share of counted spending (outflow groups) or income (inflow groups), 0–100; null when mixed or the base is 0. */
  sharePct: Nullable<number>;
}

/** An account chip for filtering the view down to one card. */
export interface AccountChip {
  accountId: string;
  label: string;
}

const PCT_MAX = 100;

// Render order + copy for each bucket. `counted` marks buckets that enter the savings
// math; the excluded ones are shown so the reader can audit what the math ignored.
const BUCKET_META: {bucket: FlowBucket; label: string; counted: boolean; note: string}[] = [
  {bucket: 'income', label: 'Income', counted: true, note: 'Counted into income'},
  {bucket: 'spending', label: 'Spending', counted: true, note: 'Counted into spending'},
  {
    bucket: 'invested',
    label: 'Invested',
    counted: true,
    note: 'Left for an investment venue — reported as invested, not spending',
  },
  {
    bucket: 'investment-return',
    label: 'Investment returns',
    counted: false,
    note: 'Capital coming back from a venue — not income',
  },
  {
    bucket: 'excluded-pair',
    label: 'Internal transfers (paired)',
    counted: false,
    note: 'Matched a debit↔credit pair between your own accounts',
  },
  {
    bucket: 'excluded-routing',
    label: 'Own money routed',
    counted: false,
    note: 'Your own money hopping between accounts via a family member — neither income nor spending',
  },
  {
    bucket: 'excluded-transfer',
    label: 'Excluded as transfers',
    counted: false,
    note: 'Carries a transfer category and matched nothing else',
  },
];

function sumUsd(items: FlowBreakdownItem[]): number {
  return items.reduce((sum, i) => sum + i.amountUsd, 0);
}

function wholeUsd(value: number): string {
  return MoneyUtils.format(value, 'USD', {minFractionDigits: 0});
}

export function flowBreakdownComputed(store: StateSignals) {
  // A `from` and/or `to` date is a dashboard window's day range (with `months` as the history the
  // dashboard loaded around it); otherwise the page shows one month, where an absent or invalid
  // `month` means "now".
  const range = computed((): Nullable<FlowBreakdownRange> => {
    const from = store.from();
    const to = store.to();
    if (from === null && to === null) {
      return null;
    }
    return {from, to: to ?? DateUtils.isoDateUtc(new Date()), months: store.months()};
  });
  const month = computed(() =>
    range() ? '' : store.selectedMonth() || MonthKeyUtils.currentUtc()
  );
  const request = computed((): FlowBreakdownRequest => {
    const window = range();
    return window ? {kind: 'range', ...window} : {kind: 'month', month: month()};
  });

  const filteredItems = computed((): FlowBreakdownItem[] => {
    const items = store.breakdown()?.items ?? [];
    const account = store.accountFilter();
    return account ? items.filter(i => i.accountId === account) : items;
  });

  const itemsOf = (bucket: FlowBucket): FlowBreakdownItem[] =>
    filteredItems().filter(i => i.bucket === bucket);

  const incomeUsd = computed(() => sumUsd(itemsOf('income')));
  const spendingUsd = computed(() => sumUsd(itemsOf('spending')));
  const investedUsd = computed(() => sumUsd(itemsOf('invested')));

  const sharePct = (items: FlowBreakdownItem[], totalUsd: number): Nullable<number> => {
    const allIn = items.every(i => i.direction === 'in');
    const allOut = items.every(i => i.direction === 'out');
    const base = allIn ? incomeUsd() : allOut ? spendingUsd() : 0;
    return base > 0 ? (totalUsd / base) * PCT_MAX : null;
  };

  return {
    range,
    month,
    request,
    isEmpty: computed(() => store.status() === 'success' && filteredItems().length === 0),
    groups: computed((): BucketGroup[] =>
      BUCKET_META.map(meta => {
        const items = itemsOf(meta.bucket);
        const totalUsd = sumUsd(items);
        return {...meta, items, totalUsd, sharePct: sharePct(items, totalUsd)};
      }).filter(g => g.items.length > 0)
    ),
    accountChips: computed((): AccountChip[] => {
      const seen = new Map<string, AccountChip>();
      for (const i of store.breakdown()?.items ?? []) {
        if (!seen.has(i.accountId)) {
          seen.set(i.accountId, {
            accountId: i.accountId,
            label: `${i.bankName} ${i.currency} ••${i.accountLast4}`,
          });
        }
      }
      return [...seen.values()].sort((a, b) => a.label.localeCompare(b.label));
    }),
    incomeFormatted: computed(() => wholeUsd(incomeUsd())),
    spendingFormatted: computed(() => wholeUsd(spendingUsd())),
    investedFormatted: computed(() => wholeUsd(investedUsd())),
    savedFormatted: computed(() => wholeUsd(incomeUsd() - spendingUsd())),
  };
}
