import {type Timestamped} from '../timestamped/timestamped.model';

/** A Ledger proposal on one name (#1069). Hold only ever answers a fired event. */
export type RecommendationAction = 'Add' | 'Hold' | 'Trim' | 'Exit';

export type RecommendationStatus = 'Pending' | 'Confirmed' | 'Rejected' | 'Expired' | 'Superseded';

/** `Ledger` = drafted by the agent; `Rules` = raised by the rule sweep (a breached cap, a broken thesis). */
export type RecommendationOrigin = 'Ledger' | 'Rules';

export type RecommendationTriggerKind =
  'ThesisBreak' | 'IpsBand' | 'RiskRule' | 'RadarSignal' | 'EventVerdict';

export type RecommendationConfidence = 'Low' | 'Medium' | 'High';

/** Whether a confirmed proposal was placed: a matching fill seen within the window after the decision. */
export type RecommendationExecution = 'Pending' | 'Executed' | 'Unexecuted' | 'NotApplicable';

export type RecommendationEvidenceKind =
  | 'Thesis'
  | 'RiskRule'
  | 'PolicyReview'
  | 'Sleeve'
  | 'RadarSignal'
  | 'Event'
  | 'Candidate'
  | 'Note';

export interface RecommendationEvidenceDto {
  kind: RecommendationEvidenceKind;
  label: string;
  refId: Nullable<string>;
}

/**
 * One proposal as `research/recommendations` and the asset dossier return it. `orderText` is display
 * text the owner places at the broker - nothing in Finance Sentry sends it. Percentages are
 * already percents (12.2 = 12.2% of book); returns are scored against the S&P 500 from the decision.
 */
export interface RecommendationDto extends Timestamped {
  id: string;
  ticker: string;
  action: RecommendationAction;
  status: RecommendationStatus;
  createdBy: string;
  origin: RecommendationOrigin;
  triggerKind: RecommendationTriggerKind;
  triggerRef: string;
  rationale: string;
  confidence: RecommendationConfidence;
  invalidationTrigger: string;
  evidence: RecommendationEvidenceDto[];
  sizeUsd: Nullable<number>;
  sizeShares: Nullable<number>;
  shareOfBookBeforePct: Nullable<number>;
  shareOfBookAfterPct: Nullable<number>;
  limitPrice: Nullable<number>;
  limitCurrency: Nullable<string>;
  venue: Nullable<string>;
  orderText: Nullable<string>;
  expiresAt: string;
  decidedAt: Nullable<string>;
  decisionNote: Nullable<string>;
  execution: RecommendationExecution;
  executedAt: Nullable<string>;
  subjectReturnPct: Nullable<number>;
  benchmarkReturnPct: Nullable<number>;
  excessReturnPct: Nullable<number>;
  /**
   * What the owner's own decision earned over the S&P 500, signed by its direction - computed on the server
   * (`RecommendationValue.DecisionValuePct`), the one definition the Ledger scoreboard also grades by. Null when
   * unscored, when a confirmed order was never placed, and for a rejected Hold.
   */
  decisionValuePct: Nullable<number>;
  outcomeScoredAt: Nullable<string>;
}

/** The Decisions queue: pending oldest first, and those closed in the last 30 days newest first. */
export interface RecommendationsResult {
  pending: RecommendationDto[];
  decided: RecommendationDto[];
}

export type RecommendationVerdict = 'confirm' | 'reject';

export interface ConfirmRecommendationRequest {
  note: Nullable<string>;
}

export interface RejectRecommendationRequest {
  reason: string;
}

/** What the decision sheet opens with. */
export interface DecisionSheetData {
  recommendation: RecommendationDto;
  verdict: RecommendationVerdict;
}
