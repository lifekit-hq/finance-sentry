export const REPORT_PROBLEM_LABEL = 'Report a problem';

/** Avatar-menu and palette id that opens the sheet. */
export const REPORT_PROBLEM_ACTION_ID = '_report_problem';

export const REPORT_PROBLEM_DRAWER_WIDTH = '480px';

/** Mirrors `ProblemReportLimits.TextMaxLength` on the API. */
export const REPORT_PROBLEM_TEXT_MAX_LENGTH = 1000;

export const REPORT_PROBLEM_TEXT_ROWS = 6;

export const REPORT_PROBLEM_HELPER = "Don't include account numbers, amounts or passwords";

export const REPORT_PROBLEM_FAILED_MESSAGE =
  "Couldn't send your report. Your text is kept; try again.";

/** The optional kind chips, in the order the sheet shows them; `value` is what the API accepts. */
export const REPORT_PROBLEM_KINDS = [
  {value: 'Broken', label: 'Broken'},
  {value: 'LooksWrong', label: 'Looks wrong'},
  {value: 'Idea', label: 'Idea'},
] as const;

/** What `RoutePatternUtils` reports when the router has nothing to name. */
export const UNKNOWN_ROUTE_PATTERN = '/';
