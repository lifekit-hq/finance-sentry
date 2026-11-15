import {type Nullable} from '@lifekit-hq/ui';
/** The verdict vocabulary (spec 726 OQ-3); `NotScored` is the non-verdict. */
export type VerdictWord = 'StrongBuy' | 'Buy' | 'Wait' | 'Avoid' | 'NotScored';

/** The scorecard axis a verdict condition reads; `LookThrough` is the ETF table's share of top-10 weight reading BUY or better. */
export type VerdictAxis =
  'Structure' | 'Fundamentals' | 'Crowding' | 'Valuation' | 'Growth' | 'LookThrough';

/** The yardstick the valuation conditions read: the P/E against its own history, or the forward P/E against peers. */
export type VerdictPriceLens = 'own_history' | 'peers';

export type VerdictOperator =
  'LessThan' | 'GreaterThan' | 'AtLeast' | 'AtMost' | 'Is' | 'IsNot' | 'IsMissing';

/** Which structure value the verdict read: the regime-adjusted one, or raw when there was no regime reading. */
export type VerdictStructureBasis = 'adjusted' | 'raw';

/** One threshold test with the value it read; `actual` null means the axis is missing. */
export interface VerdictConditionDto {
  axis: VerdictAxis;
  operator: VerdictOperator;
  threshold: Nullable<string>;
  actual: Nullable<string>;
  met: boolean;
}

/** One rule of the ordered table: matched when every condition is met, fired when it is the first match. */
export interface VerdictRuleDto {
  id: string;
  word: VerdictWord;
  description: string;
  matched: boolean;
  fired: boolean;
  conditions: VerdictConditionDto[];
}

/** How an axis stands against the bars: clears them, falls short, has nothing to read, or the rules do not apply. */
export type VerdictReasonStatus = 'Pass' | 'Fail' | 'Missing' | 'NotApplicable';

/** One fact behind the word as a sentence, the value against the rule's bar; `decisive` when the fired rule tested the axis. */
export interface VerdictReasonDto {
  axis: VerdictAxis;
  status: VerdictReasonStatus;
  text: string;
  decisive: boolean;
}

export interface VerdictDto {
  word: VerdictWord;
  rulesVersion: number;
  firedRuleId: string;
  structureBasis: VerdictStructureBasis;
  rules: VerdictRuleDto[];
  /** Derived from the rule table by the backend, never written by a model. One per axis for a scored name. */
  reasons: VerdictReasonDto[];
  /** What has to change for the BUY rules to fire; null when already BUY or better, or not scored. */
  turnsBuyWhen: Nullable<string>;
  /** Why a WAIT waits (a code); absent for every other word and on payloads from before rules v2. */
  waitReason?: Nullable<string>;
  /** The short label of `waitReason`, ready to show beside the word. */
  waitReasonLabel?: Nullable<string>;
  /** Which yardstick the valuation conditions read; null when the price lens had nothing to read. */
  priceLens?: Nullable<VerdictPriceLens>;
}

/**
 * Why a symbol has no verdict to show (issue #1414): the caller is not the owner (`no_role`), the name is
 * outside what the verdict scores (`outside_universe`), or the read failed and may work again (`unreadable`).
 */
export type ScorecardGapReason = 'no_role' | 'outside_universe' | 'unreadable';

/**
 * One symbol's verdict word from the batch read behind the Accounts rows, with what would turn it into BUY.
 * A symbol whose verdict could not be read keeps its row with no word and a `gapReason`, so its chip says why.
 */
export interface ScorecardVerdictSummary {
  symbol: string;
  word: Nullable<VerdictWord>;
  firedRuleId: Nullable<string>;
  /** Set only when there is no word; absent on payloads from before #1414. */
  gapReason?: Nullable<ScorecardGapReason>;
  /** The same line the full scorecard carries; null when there is no bar to name (BUY or better, or not scored). */
  turnsBuyWhen: Nullable<string>;
  /** The named reason of a WAIT, as a short label. */
  waitReasonLabel?: Nullable<string>;
}

/** Fractions stay fractions (0.12 = 12%); window keys are trading days. */
export interface ScorecardStructureAxis {
  rawScore: Nullable<number>;
  adjustedScore: Nullable<number>;
  adjustmentPoints: number;
  regimeRationale: string[];
  volatilityRegime: Nullable<string>;
  ratesRegime: Nullable<string>;
  regimeAsOf: Nullable<string>;
  rsByWindow: Record<string, Nullable<number>>;
  returnByWindow: Record<string, Nullable<number>>;
  extensionFromMa50: Nullable<number>;
  todayZScore: Nullable<number>;
  volumeRatio: Nullable<number>;
  sectorRank: Nullable<number>;
  sectorRankDelta: Nullable<number>;
  distanceFrom63dHigh: Nullable<number>;
  notEvaluableReasons: string[];
}

export interface ScorecardFundamentalsAxis {
  score: Nullable<number>;
  revenueYoy: Nullable<number>;
  grossMarginLatest: Nullable<number>;
  grossMarginTrend: Nullable<number>;
  epsYoy: Nullable<number>;
  basis: Nullable<string>;
  /** `annual_fallback` when EPS growth was read from fiscal years because the quarters could not resolve it. */
  epsYoyBasis?: Nullable<string>;
  notEvaluableReasons: string[];
}

/** `class` is null when there is no extension to classify: an unknown axis, not Normal. */
export interface ScorecardCrowdingAxis {
  class: Nullable<'Early' | 'Normal' | 'Extended'>;
  extensionFromMa50: Nullable<number>;
  volumeRatio: Nullable<number>;
  earlyExtensionThreshold: number;
  extendedExtensionThreshold: number;
  extendedVolumeRatioThreshold: number;
  notEvaluableReason: Nullable<string>;
}

/** Raw facts, never a 0-100 score. `premiumPct` and `impliedUpsidePct` are percents (29.2 = 29.2%). */
export interface ScorecardValuationAxis {
  notApplicable: boolean;
  trailingPe: Nullable<number>;
  trailingPeFiveYearAvg: Nullable<number>;
  historyWindowYears: Nullable<number>;
  premiumPct: Nullable<number>;
  forwardPe: Nullable<number>;
  price: Nullable<number>;
  consensusTarget: Nullable<number>;
  impliedUpsidePct: Nullable<number>;
  currency: Nullable<string>;
  isStale: boolean;
  notEvaluableReason: Nullable<string>;
  /** The median forward P/E of the peers, their count and the forward P/E's premium over it; set only when the own history is missing. */
  peerForwardPeMedian?: Nullable<number>;
  peerCount?: Nullable<number>;
  peerPremiumPct?: Nullable<number>;
  priceLens?: Nullable<VerdictPriceLens>;
}

/** How much of an ETF's top-10 look-through could be read: every holding scored, some not, or no holdings found. */
export type LookThroughStatus = 'complete' | 'partial' | 'unavailable';

/** Where the holdings came from: Yahoo `topHoldings`, or the issuer's published holdings file. */
export type LookThroughSource = 'yahoo' | 'issuer';

/** One top holding of an ETF with the word the single-name rules gave it; `word` is null when it could not be scored. */
export interface LookThroughHoldingDto {
  symbol: Nullable<string>;
  name: Nullable<string>;
  /** Percent of the fund (7.7 = 7.7%). */
  weightPct: number;
  word: Nullable<VerdictWord>;
  firedRuleId: Nullable<string>;
}

/**
 * The basis of an ETF verdict (issue #1324): the fund's top holdings, each scored by the single-name rules, and the
 * share of their weight reading BUY or better. Weights and shares are percents. `unavailable` means no holdings were
 * found, so the look-through was not applied and the word cannot reach BUY; `note` says what is missing in words.
 */
export interface ScorecardLookThroughDto {
  status: LookThroughStatus;
  source: Nullable<LookThroughSource>;
  asOf: Nullable<string>;
  coveredWeightPct: number;
  buyWeightPct: number;
  buyWeightSharePct: Nullable<number>;
  buyWeightSharePctRequired: number;
  holdings: LookThroughHoldingDto[];
  note: Nullable<string>;
}

/**
 * One symbol's four axes and the verdict word they map to (spec 726 US2). There is no composite
 * number: the axes stay separate and the word comes from a published rule table.
 */
export interface ScorecardDto {
  symbol: string;
  generatedAt: string;
  formulaVersion: number;
  structure: ScorecardStructureAxis;
  fundamentals: ScorecardFundamentalsAxis;
  crowding: ScorecardCrowdingAxis;
  valuation: ScorecardValuationAxis;
  verdict: VerdictDto;
  /** Set for an ETF only: the top holdings its word was read through. Absent from older API builds. */
  lookThrough?: Nullable<ScorecardLookThroughDto>;
}

/** One fact behind an axis, already formatted for display. */
export interface ScorecardFact {
  label: string;
  value: string;
}

/** One axis as the drill-down shows it: a headline value, the facts behind it and any notes. */
export interface ScorecardAxisView {
  axis: VerdictAxis;
  label: string;
  summary: string;
  facts: ScorecardFact[];
  notes: string[];
}

/** One rule as a step of the audit trail: its place in the order, then the rule as the rule table shows it. */
export interface VerdictTrailStep extends VerdictRuleView {
  number: number;
}

/** One condition as the rule table shows it. */
export interface VerdictConditionView {
  text: string;
  actual: string;
  met: boolean;
}

/** One rule as the rule table shows it. */
export interface VerdictRuleView {
  id: string;
  word: VerdictWord;
  description: string;
  matched: boolean;
  fired: boolean;
  conditions: VerdictConditionView[];
}

/** One look-through holding as the basis shows it. */
export interface LookThroughHoldingView {
  label: string;
  name: Nullable<string>;
  weight: string;
  word: VerdictWord;
  scored: boolean;
}

/** An ETF's look-through basis as the drill-down shows it; `degraded` when it is not the full top 10 read. */
export interface LookThroughView {
  headline: string;
  sourceLine: Nullable<string>;
  degraded: boolean;
  note: Nullable<string>;
  holdings: LookThroughHoldingView[];
}
