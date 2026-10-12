import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {REPORT_PROBLEM_FAILED_MESSAGE} from '../constants/report-problem.constants';
import {type ReportProblemState} from './report-problem.state';

interface StateSignals {
  status: Signal<ReportProblemState['status']>;
  errorCode: Signal<ReportProblemState['errorCode']>;
}

export function reportProblemComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  return {
    isSubmitting: computed(() => store.status() === 'submitting'),
    errorMessage: computed(() =>
      store.status() === 'error'
        ? (errorMessages.resolve(store.errorCode()) ?? REPORT_PROBLEM_FAILED_MESSAGE)
        : ''
    ),
  };
}
