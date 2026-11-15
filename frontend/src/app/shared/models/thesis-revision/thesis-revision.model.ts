import {type Nullable} from '@lifekit-hq/ui';

import {
  type ThesisCatalyst,
  type ThesisDataPoint,
  type ThesisDto,
  type ThesisInvalidationTrigger,
} from '../thesis/thesis.model';
import {type Timestamped} from '../timestamped/timestamped.model';

/** What a proposal asks the owner to do: promote a candidate, change a thesis's narrative or triggers, or retire it. */
export type ThesisRevisionKind = 'Promote' | 'NarrativeUpdate' | 'TriggerChange' | 'Close';

export type ThesisRevisionStatus = 'Pending' | 'Confirmed' | 'Rejected' | 'Expired' | 'Superseded';

/** What a proposal would write. A null field is left as it is (on a promotion: defaulted from the scorecard). */
export interface ThesisRevisionContent {
  thesisText: Nullable<string>;
  keyDataPoints: Nullable<ThesisDataPoint[]>;
  catalysts: Nullable<ThesisCatalyst[]>;
  invalidationTriggers: Nullable<ThesisInvalidationTrigger[]>;
  narrativeFalsifiers: Nullable<string[]>;
  entryPrice: Nullable<number>;
}

/** References into the stored research the proposal rests on. */
export interface ThesisEvidenceRefs {
  newsArticleIds: Nullable<string[]>;
  filingAccessions: Nullable<string[]>;
  eventIds: Nullable<string[]>;
  fundamentalsAsOf: Nullable<string>;
}

export type ThesisEvidenceLinkKind = 'News' | 'Filing' | 'Event' | 'Fundamentals';

/** One piece of evidence as the card shows it; only a stored news article carries a link. */
export interface ThesisEvidenceLink {
  kind: ThesisEvidenceLinkKind;
  label: string;
  url: Nullable<string>;
}

/** One proposal as `research/thesis-revisions` returns it. Confirming it is the single step that writes the thesis. */
export interface ThesisRevisionDto extends Timestamped {
  id: string;
  kind: ThesisRevisionKind;
  status: ThesisRevisionStatus;
  /** Who put it forward: `Ledger` or `User`. */
  proposedBy: string;
  subjectType: string;
  /** A candidate (Promote) or a thesis (every other kind). */
  subjectId: string;
  ticker: string;
  proposed: ThesisRevisionContent;
  rationale: string;
  evidence: Nullable<ThesisEvidenceRefs>;
  /** Filled on the queue read only. */
  evidenceLinks: Nullable<ThesisEvidenceLink[]>;
  proposedUsd: number;
  paper: boolean;
  expiresAt: string;
  decidedAt: Nullable<string>;
  decisionNote: Nullable<string>;
  resultThesisId: Nullable<string>;
}

/** The thesis-revision queue: pending oldest first, and those closed in the last 30 days. */
export interface ThesisRevisionsResult {
  pending: ThesisRevisionDto[];
  decided: ThesisRevisionDto[];
}

export interface ConfirmThesisRevisionRequest {
  note: Nullable<string>;
  /** The owner's explicit override of a refused promotion's risk gate. */
  overrideRisk: boolean;
}

export interface RejectThesisRevisionRequest {
  reason: string;
}

/** The risk gate's reading of a promotion it refused. */
export interface ThesisRiskGateVerdict {
  ruleKey: Nullable<string>;
  observedValue: Nullable<number>;
  limitValue: Nullable<number>;
  maxCompliantSizeUsd: Nullable<number>;
  note: Nullable<string>;
}

/** The answer to a confirm or reject; a refused promotion answers 409 with the same body and stays pending. */
export interface DecideThesisRevisionResult {
  revision: ThesisRevisionDto;
  thesis: Nullable<ThesisDto>;
  gate: Nullable<ThesisRiskGateVerdict>;
  note: Nullable<string>;
}

export type ThesisRevisionVerdict = 'confirm' | 'reject';

/** What the revision sheet opens with. */
export interface ThesisRevisionSheetData {
  revision: ThesisRevisionDto;
  verdict: ThesisRevisionVerdict;
}

/** How a card field changes; lists carry what is added, removed and kept, text the two versions side by side. */
export type ThesisRevisionChangeField =
  'text' | 'triggers' | 'dataPoints' | 'catalysts' | 'falsifiers' | 'entryPrice';

export interface ThesisRevisionChange {
  field: ThesisRevisionChangeField;
  label: string;
  /** Text and entry price show before and after; a list shows what was added and removed. */
  shape: 'text' | 'list';
  before: Nullable<string>;
  after: Nullable<string>;
  added: string[];
  removed: string[];
  kept: string[];
}
