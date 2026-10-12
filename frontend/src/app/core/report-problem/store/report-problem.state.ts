import {type ProblemReportKind, type ReportProblemStatus} from '../models/report-problem.model';

export interface ReportProblemState {
  kind: Nullable<ProblemReportKind>;
  status: ReportProblemStatus;
  errorCode: Nullable<string>;
}

export const initialReportProblemState: ReportProblemState = {
  kind: null,
  status: 'idle',
  errorCode: null,
};
