import {type Nullable} from '@lifekit-hq/ui';
/** The verdict vocabulary (spec 726 OQ-3); `NotScored` is the non-verdict. */
export type VerdictWord = 'StrongBuy' | 'Buy' | 'Wait' | 'Avoid' | 'NotScored';

/** The scorecard axis a verdict condition reads. */
export type VerdictAxis = 'Structure' | 'Fundamentals' | 'Crowding' | 'Valuation';

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

export interface VerdictDto {
  word: VerdictWord;
  rulesVersion: number;
  firedRuleId: string;
  structureBasis: VerdictStructureBasis;
  rules: VerdictRuleDto[];
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
