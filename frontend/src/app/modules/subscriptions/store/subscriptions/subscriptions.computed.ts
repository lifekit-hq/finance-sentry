import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

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
  addErrorCode: Signal<Nullable<string>>;
  addErrorDetail: Signal<Nullable<string>>;
}

/** The only add error whose server text (it names the row that holds the charges) is shown. */
const ALREADY_TRACKED_CODE = 'COMMITMENT_ALREADY_TRACKED';

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

/** The expected charge date has passed and no charge like it has arrived since. */
const isMissed = (s: Subscription): boolean => SubscriptionUtils.daysUntil(s.nextExpectedDate) < 0;

const isDueThisWeek = (s: Subscription): boolean => {
  const days = SubscriptionUtils.daysUntil(s.nextExpectedDate);
  return days >= 0 && days <= DUE_THIS_WEEK_DAYS;
};

const isSubscription = (s: Subscription): boolean => s.kind === 'subscription';
const isInstallment = (s: Subscription): boolean => s.kind === 'installment';

export function subscriptionsComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  return {
    addErrorMessage: computed(() => {
      const code = store.addErrorCode();
      if (code === null) {
        return '';
      }
      const detail = code === ALREADY_TRACKED_CODE ? store.addErrorDetail() : null;
      return detail ?? errorMessages.resolve(code) ?? 'Failed to add.';
    }),
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
    /**
     * Active subscriptions split into "Due this week", "Charge missed" (the expected date passed
     * without a charge) and "Later", plus the legacy hand-typed rows that follow no charges: their
     * dates are only what was typed, so they are never "due" or "missed". An overdue row is never
     * "due this week".
     */
    activeSections: computed((): SubscriptionSection[] => {
      const all = sortBy(
        store.subscriptions().filter(s => s.status === 'active' && isSubscription(s)),
        store.sort()
      );
      const active = all.filter(s => s.isTracked);
      return (
        [
          {id: 'due', label: 'Due this week', items: active.filter(isDueThisWeek)},
          {id: 'missed', label: 'Charge missed', items: active.filter(isMissed)},
          {
            id: 'later',
            label: 'Later',
            items: active.filter(s => !isDueThisWeek(s) && !isMissed(s)),
          },
          {
            id: 'unlinked',
            label: 'Not linked to transactions',
            items: all.filter(s => !s.isTracked),
          },
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
