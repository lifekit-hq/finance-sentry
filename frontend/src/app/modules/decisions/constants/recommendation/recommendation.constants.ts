/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type TagVariant} from '@lifekit-hq/ui';

import {
  type RecommendationAction,
  type RecommendationConfidence,
  type RecommendationEvidenceKind,
  type RecommendationStatus,
  type RecommendationTriggerKind,
} from '../../../../shared/models/recommendation/recommendation.model';

export const RECOMMENDATION_ACTION_LABEL: Readonly<Record<RecommendationAction, string>> = {
  Add: 'Add',
  Hold: 'Hold',
  Trim: 'Trim',
  Exit: 'Exit',
};

// Per-axis, never a buy/sell verdict badge: the tag only names the move, the card carries the evidence.
export const RECOMMENDATION_ACTION_VARIANT: Readonly<Record<RecommendationAction, TagVariant>> = {
  Add: 'info',
  Hold: 'neutral',
  Trim: 'warning',
  Exit: 'warning',
};

export const RECOMMENDATION_STATUS_LABEL: Readonly<Record<RecommendationStatus, string>> = {
  Pending: 'Pending',
  Confirmed: 'Confirmed',
  Rejected: 'Rejected',
  Expired: 'Expired',
  Superseded: 'Superseded',
};

export const RECOMMENDATION_STATUS_VARIANT: Readonly<Record<RecommendationStatus, TagVariant>> = {
  Pending: 'info',
  Confirmed: 'success',
  Rejected: 'neutral',
  Expired: 'neutral',
  Superseded: 'neutral',
};

export const RECOMMENDATION_CONFIDENCE_LABEL: Readonly<Record<RecommendationConfidence, string>> = {
  Low: 'Low confidence',
  Medium: 'Medium confidence',
  High: 'High confidence',
};

export const RECOMMENDATION_TRIGGER_LABEL: Readonly<Record<RecommendationTriggerKind, string>> = {
  ThesisBreak: 'Thesis break',
  IpsBand: 'Outside its IPS band',
  RiskRule: 'Risk rule breached',
  RadarSignal: 'Radar signal',
  EventVerdict: 'Event verdict',
};

export const RECOMMENDATION_EVIDENCE_LABEL: Readonly<Record<RecommendationEvidenceKind, string>> = {
  Thesis: 'Thesis',
  RiskRule: 'Risk rule',
  PolicyReview: 'Policy review',
  Sleeve: 'Sleeve',
  RadarSignal: 'Radar',
  Event: 'Event',
  Candidate: 'Candidate',
  Note: 'Note',
};

/** The no-trade wall: Finance Sentry records the decision; the owner places any order at the broker. */
export const NO_TRADE_WALL_TITLE = "Finance Sentry won't place this.";
export const NO_TRADE_WALL_TEXT =
  'Open IBKR and place it yourself. Confirming only records your decision.';
export const HOLD_NO_ORDER_TEXT =
  'Hold has no order: confirming records that you keep the position.';

export const DECISION_NOTE_MAX_LENGTH = 2000;
export const DECISION_TEXT_ROWS = 3;
export const DECISION_SHEET_WIDTH = '28rem';
export const DECISION_FAILED_MESSAGE = 'Could not record the decision. Try again.';
export const DECISIONS_LOAD_FAILED_MESSAGE = 'Could not load the decisions queue.';
export const ORDER_COPIED_MESSAGE = 'Order text copied.';
export const DECISION_SKELETON_ROWS = 2;
export const DECISION_SKELETON_HEIGHT = '12rem';

export const DECISIONS_EMPTY_COPY = {
  message: 'No proposals yet',
  subMessage:
    'Ledger stays silent until a broken thesis, a breached rule, a band or a fired event calls for a move.',
  icon: 'Inbox',
} as const;

/** The dossier's note once its proposal is decided; the card's place says what was recorded. */
export const DECISION_RECORDED_TITLE: Readonly<Record<RecommendationStatus, string>> = {
  Pending: 'Decision pending',
  Confirmed: 'Decision recorded: confirmed',
  Rejected: 'Decision recorded: rejected',
  Expired: 'Proposal expired',
  Superseded: 'Proposal superseded',
};
