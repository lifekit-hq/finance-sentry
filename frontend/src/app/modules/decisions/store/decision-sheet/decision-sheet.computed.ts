import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {DECISION_FAILED_MESSAGE} from '../../constants/recommendation/recommendation.constants';
import {type DecisionSheetState} from './decision-sheet.state';

interface StateSignals {
  status: Signal<DecisionSheetState['status']>;
  errorCode: Signal<DecisionSheetState['errorCode']>;
}

export function decisionSheetComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  return {
    isSubmitting: computed(() => store.status() === 'submitting'),
    errorMessage: computed(() =>
      store.status() === 'error'
        ? (errorMessages.resolve(store.errorCode()) ?? DECISION_FAILED_MESSAGE)
        : ''
    ),
  };
}
