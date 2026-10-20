import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withMethods, withState} from '@ngrx/signals';

import {REPORT_PROBLEM_FAILED_MESSAGE} from '../constants/report-problem.constants';
import {reportProblemEffects} from './report-problem.effects';
import {reportProblemMethods} from './report-problem.methods';
import {initialReportProblemState} from './report-problem.state';

/** Provided on the sheet: it lives and dies with the drawer. */
export const ReportProblemStore = signalStore(
  withState(initialReportProblemState),
  withAsyncStatus({defaultErrorMessage: REPORT_PROBLEM_FAILED_MESSAGE}),
  withMethods(reportProblemMethods),
  withMethods(reportProblemEffects)
);
