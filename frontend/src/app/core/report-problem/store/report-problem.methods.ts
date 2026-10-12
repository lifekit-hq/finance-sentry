import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type ProblemReportKind, type ReportProblemStatus} from '../models/report-problem.model';
import {type ReportProblemState} from './report-problem.state';

export function reportProblemMethods(store: WritableStateSource<ReportProblemState>) {
  return {
    /** Picking the chip again clears it: the kind is optional. */
    toggleKind(kind: ProblemReportKind): void {
      patchState(store, state => ({kind: state.kind === kind ? null : kind}));
    },
    setStatus(status: ReportProblemStatus): void {
      patchState(store, {status});
    },
  };
}
