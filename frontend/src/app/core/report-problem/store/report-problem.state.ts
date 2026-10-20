import {type Nullable} from '@lifekit-hq/ui';

import {type ProblemReportKind} from '../models/report-problem.model';

export interface ReportProblemState {
  kind: Nullable<ProblemReportKind>;
}

export const initialReportProblemState: ReportProblemState = {
  kind: null,
};
