import {computed, type Signal} from '@angular/core';

import {
  REPORT_PROBLEM_FAILED_MESSAGE,
  REPORT_PROBLEM_RATE_LIMITED_MESSAGE,
} from '../constants/report-problem.constants';
import {type ReportProblemState} from './report-problem.state';

interface StateSignals {
  status: Signal<ReportProblemState['status']>;
}

export function reportProblemComputed(store: StateSignals) {
  return {
    isSubmitting: computed(() => store.status() === 'submitting'),
    errorMessage: computed(() => {
      switch (store.status()) {
        case 'rate-limited':
          return REPORT_PROBLEM_RATE_LIMITED_MESSAGE;
        case 'error':
          return REPORT_PROBLEM_FAILED_MESSAGE;
        default:
          return '';
      }
    }),
  };
}
