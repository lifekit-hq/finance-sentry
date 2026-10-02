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
