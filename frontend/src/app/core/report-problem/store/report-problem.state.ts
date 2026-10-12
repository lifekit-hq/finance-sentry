import {type ProblemReportKind, type ReportProblemStatus} from '../models/report-problem.model';

export interface ReportProblemState {
  kind: Nullable<ProblemReportKind>;
  status: ReportProblemStatus;
}

export const initialReportProblemState: ReportProblemState = {
  kind: null,
  status: 'idle',
};
