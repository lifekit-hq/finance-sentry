import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {
  type FlowBreakdown,
  type FlowBreakdownItem,
  type FlowBucket,
} from '../../models/flow-breakdown/flow-breakdown.model';

interface StateSignals {
  breakdown: Signal<Nullable<FlowBreakdown>>;
  month: Signal<string>;
  accountFilter: Signal<Nullable<string>>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
}

/** One rendered section of the audit view: a bucket, its rows, and their USD sum. */
export interface BucketGroup {
  bucket: FlowBucket;
  label: string;
  counted: boolean;
  note: string;
  items: FlowBreakdownItem[];
  totalUsd: number;
}

/** An account chip for filtering the view down to one card. */
export interface AccountChip {
  accountId: string;
  label: string;
}

const DEFAULT_ERROR = 'Failed to load the month breakdown. Please try again.';

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
  const errorMessages = inject(ErrorMessageService);

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

  return {
    isLoading: computed(() => store.status() === 'loading'),
    isEmpty: computed(() => store.status() === 'idle' && filteredItems().length === 0),
    errorMessage: computed(() => {
      if (store.status() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.errorCode()) ?? DEFAULT_ERROR;
    }),
    groups: computed((): BucketGroup[] =>
      BUCKET_META.map(meta => {
        const items = itemsOf(meta.bucket);
        return {...meta, items, totalUsd: sumUsd(items)};
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
