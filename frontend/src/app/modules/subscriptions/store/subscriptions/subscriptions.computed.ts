import {computed, type Signal} from '@angular/core';

import {DUE_THIS_WEEK_DAYS} from '../../constants/subscription/subscription-form.constants';
import {
  type Subscription,
  type SubscriptionSection,
  type SubscriptionSort,
  type SubscriptionSummary,
} from '../../models/subscription/subscription.model';
import {SubscriptionUtils} from '../../utils/subscription.utils';

interface StateSignals {
  subscriptions: Signal<Subscription[]>;
  sort: Signal<SubscriptionSort>;
  summary: Signal<Nullable<SubscriptionSummary>>;
}

function sortBy(items: Subscription[], sort: SubscriptionSort): Subscription[] {
  return [...items].sort((a, b) => {
    if (sort === 'amount') {
      return b.monthlyEquivalent - a.monthlyEquivalent;
    }
    if (sort === 'name') {
      return a.merchantName.localeCompare(b.merchantName);
    }
    return a.nextExpectedDate.localeCompare(b.nextExpectedDate);
  });
}

const isDueThisWeek = (s: Subscription): boolean =>
  SubscriptionUtils.daysUntil(s.nextExpectedDate) <= DUE_THIS_WEEK_DAYS;

const isSubscription = (s: Subscription): boolean => s.kind === 'subscription';
const isInstallment = (s: Subscription): boolean => s.kind === 'installment';

export function subscriptionsComputed(store: StateSignals) {
  return {
    activeSubscriptions: computed(() =>
      store.subscriptions().filter(s => s.status === 'active' && isSubscription(s))
    ),
    dismissedSubscriptions: computed(() =>
      store.subscriptions().filter(s => s.status === 'dismissed' && isSubscription(s))
    ),
    potentiallyCancelledSubscriptions: computed(() =>
      store.subscriptions().filter(s => s.status === 'potentially_cancelled' && isSubscription(s))
    ),
    activeInstallments: computed(() =>
      store.subscriptions().filter(s => s.status === 'active' && isInstallment(s))
    ),
    completedInstallments: computed(() =>
      store.subscriptions().filter(s => s.status === 'completed' && isInstallment(s))
    ),
    /** Active subscriptions split into a "Due this week" block and a "Later" block. */
    activeSections: computed((): SubscriptionSection[] => {
      const active = sortBy(
        store.subscriptions().filter(s => s.status === 'active' && isSubscription(s)),
        store.sort()
      );
      return (
        [
          {id: 'due', label: 'Due this week', items: active.filter(isDueThisWeek)},
          {id: 'later', label: 'Later', items: active.filter(s => !isDueThisWeek(s))},
        ] satisfies SubscriptionSection[]
      ).filter(section => section.items.length > 0);
    }),
    sortedInstallments: computed((): Subscription[] =>
      sortBy(
        store.subscriptions().filter(s => s.status === 'active' && isInstallment(s)),
        'amount'
      )
    ),
    sortedDismissed: computed((): Subscription[] =>
      sortBy(
        store.subscriptions().filter(s => s.status === 'dismissed' && isSubscription(s)),
        store.sort()
      )
    ),
  };
}
