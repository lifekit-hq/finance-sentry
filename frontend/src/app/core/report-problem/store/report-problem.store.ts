import {signalStore, withComputed, withMethods, withState} from '@ngrx/signals';

import {reportProblemComputed} from './report-problem.computed';
import {reportProblemEffects} from './report-problem.effects';
import {reportProblemMethods} from './report-problem.methods';
import {initialReportProblemState} from './report-problem.state';

/** Provided on the sheet: it lives and dies with the drawer. */
export const ReportProblemStore = signalStore(
  withState(initialReportProblemState),
  withMethods(reportProblemMethods),
  withComputed(reportProblemComputed),
  withMethods(reportProblemEffects)
);
