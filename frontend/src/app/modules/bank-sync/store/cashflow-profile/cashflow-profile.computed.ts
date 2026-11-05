import {computed, inject, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {type CashflowProfile} from '../../models/cashflow-profile/cashflow-profile.model';
import {CashflowProfileUtils} from '../../utils/cashflow-profile.utils';

interface StateSignals {
  profile: Signal<Nullable<CashflowProfile>>;
  isLoading: Signal<boolean>;
  errorMessage: Signal<string>;
}

export function cashflowProfileComputed(store: StateSignals) {
  const connectivity = inject(ConnectivityService);
  const hasData = computed(() => store.profile() !== null);

  return {
    hasData,
    // Reloads in place: a retry keeps the figures; offline only counts while nothing is on screen.
    view: computed(() =>
      AsyncViewUtils.resolve({
        isLoading: store.isLoading(),
        hasData: hasData(),
        errorMessage: store.errorMessage(),
        offline: connectivity.offline() && !hasData(),
        lastSyncedAt: null,
      })
    ),
    // The profile only while it holds figures; a withheld one (InsufficientHistory) renders the empty state instead.
    figures: computed(() => {
      const profile = store.profile();
      return profile?.status === 'InsufficientHistory' ? null : profile;
    }),
    // Under three complete months, or with no income across them, the backend withholds every figure.
    isEmpty: computed(() => store.profile()?.status === 'InsufficientHistory'),
    emptySubMessage: computed(() => CashflowProfileUtils.emptySubMessage(store.profile())),
    rate: computed(() => {
      const rate = store.profile()?.savingsRate;
      return rate == null ? null : CashflowProfileUtils.rateLabel(rate);
    }),
    rateNote: computed(() => {
      const rate = store.profile()?.savingsRate;
      return rate == null ? null : CashflowProfileUtils.rateNote(rate);
    }),
    runway: computed(() => {
      const months = store.profile()?.runwayMonths;
      return months == null ? null : CashflowProfileUtils.runwayLabel(months);
    }),
    runwayNote: computed(() => {
      const profile = store.profile();
      return profile ? CashflowProfileUtils.runwayNote(profile) : null;
    }),
    assumptions: computed(() => {
      const profile = store.profile();
      return profile ? CashflowProfileUtils.assumptions(profile) : '';
    }),
    hasStaleSleeves: computed(() => store.profile()?.hasStaleSleeves ?? false),
  };
}
