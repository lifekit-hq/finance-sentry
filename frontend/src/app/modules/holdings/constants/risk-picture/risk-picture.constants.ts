/** A fraction arrives from the API; the panel reads it in percentage points. */
export const RISK_PERCENT_POINTS = 100;

/** Weights and rates read to one decimal place: "37.8%". */
export const RISK_PERCENT_DIGITS = 1;

/** Beta reads to two decimal places: "1.34". */
export const RISK_BETA_DIGITS = 2;

export const RISK_EXPLANATIONS = {
  volatility: 'How much the book swings from month to month, scaled to a year. Higher is bumpier.',
  maxDrawdown:
    'The deepest fall of the book from a prior high, with deposits and withdrawals taken out.',
  beta: 'How far the book moves when the S&P 500 moves: 1 moves with it, above 1 moves more.',
  top1: 'The weight of your single largest position in the whole book.',
  top3: 'The weight of your three largest positions together.',
  crypto: 'The share of the whole book that is crypto.',
} as const;

export const RISK_BASIS = {
  /** Shared by the three M8 figures (money-semantics §5b, Risk statistics M8). */
  returns:
    'Monthly time-weighted returns of the brokerage book (cash included, deposits and withdrawals taken out)',
  beta: 'Monthly returns of the brokerage book against SPY adjusted close, over the months both have',
  weights:
    'Invested positions at current value as a share of the whole book, cash included; one symbol across venues is one position',
} as const;

/** Why a figure has no number, in words; unknown reasons fall back to the generic line. */
export const RISK_REASON_LABELS: ReadonlyMap<string, string> = new Map([
  ['NotEnoughHistory', 'not enough history yet'],
  ['InsufficientData', 'not enough history yet'],
  ['InvalidInput', 'the benchmark did not move'],
  ['MissingValue', 'a month of value is missing'],
]);

export const RISK_REASON_FALLBACK = 'not measured';

export const RISK_TOLERANCE_NOT_SET = 'tolerance not set';
