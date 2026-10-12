export type ProblemReportKind = 'Broken' | 'LooksWrong' | 'Idea';

export type ProblemReportDevice = 'Phone' | 'Desktop';

/** The body of `POST /api/v1/feedback`: the client part of a report and nothing else. */
export interface ProblemReportRequest {
  kind: Nullable<ProblemReportKind>;
  text: Nullable<string>;
  /** The route pattern from the router config (`/accounts/:id`), never the real URL. */
  route: string;
  appVersion: string;
  device: ProblemReportDevice;
  os: Nullable<string>;
  browser: Nullable<string>;
}

export interface ProblemReportAccepted {
  /** What the person can quote, such as `FS-R-42`. */
  reference: string;
}

export type ReportProblemStatus = 'idle' | 'submitting' | 'error' | 'rate-limited';
