export const COVERAGE_LABELS: Readonly<Record<string, string>> = {
  inUniverse: 'In your research universe',
  notInUniverse: 'Outside your research universe',
  marketWide: 'Market-wide',
};

/* eslint-disable @typescript-eslint/naming-convention -- keys are the backend's raw signal-type codes */
export const SIGNAL_TYPE_LABELS: Readonly<Record<string, string>> = {
  RSI_OVERSOLD: 'RSI oversold',
  RSI_OVERBOUGHT: 'RSI overbought',
  VOLUME_SPIKE: 'Volume spike',
  candidate_scored: 'Candidate scored',
  top_tier_candidate: 'Top-tier candidate',
  promotion_risk_override: 'Promotion risk override',
  risk_override: 'Risk override',
};
/* eslint-enable @typescript-eslint/naming-convention */

/* eslint-disable @typescript-eslint/naming-convention -- keys are the backend's raw analyst action codes */
export const ANALYST_ACTION_LABELS: Readonly<Record<string, string>> = {
  Upgrade: 'Upgrade',
  Downgrade: 'Downgrade',
  Initiate: 'Initiated',
  TargetChange: 'Target changed',
  Reiterate: 'Reiterated',
  TopIdea: 'Top idea',
};
/* eslint-enable @typescript-eslint/naming-convention */

/** News `source` is a feed label, not a name: the Yahoo ticker feeds arrive as `yahoo:GRAB`. */
export const NEWS_SOURCE_PREFIX_LABELS: Readonly<Record<string, string>> = {
  'yahoo:': 'Yahoo Finance',
};

export const NEWS_SOURCE_LABELS: Readonly<Record<string, string>> = {
  'fed-press': 'Federal Reserve',
};

export const PEER_SET_SECTOR_PREFIX = 'sector:';
export const PEER_SET_DEFAULT_SUFFIX = ' (default)';
export const PEER_SET_LABELS: Readonly<Record<string, string>> = {
  default: 'Similar companies',
  custom: 'Your peer set',
};
