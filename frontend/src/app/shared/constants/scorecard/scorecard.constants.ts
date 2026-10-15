/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type TagVariant} from '@lifekit-hq/ui';

import {
  type VerdictAxis,
  type VerdictOperator,
  type VerdictStructureBasis,
  type VerdictWord,
} from '../../models/scorecard/scorecard.model';

export const VERDICT_WORD_LABEL: Readonly<Record<VerdictWord, string>> = {
  StrongBuy: 'STRONG BUY',
  Buy: 'BUY',
  Wait: 'WAIT',
  Avoid: 'AVOID',
  NotScored: 'NOT SCORED',
};

export const VERDICT_WORD_VARIANT: Readonly<Record<VerdictWord, TagVariant>> = {
  StrongBuy: 'success',
  Buy: 'success',
  Wait: 'neutral',
  Avoid: 'error',
  NotScored: 'neutral',
};

export const VERDICT_AXIS_LABEL: Readonly<Record<VerdictAxis, string>> = {
  Structure: 'Structure',
  Fundamentals: 'Fundamentals',
  Crowding: 'Crowding',
  Valuation: 'Valuation',
};

/** What a condition on each axis reads, as the rule table names it. */
export const VERDICT_CONDITION_SUBJECT: Readonly<Record<VerdictAxis, string>> = {
  Structure: 'Adjusted structure',
  Fundamentals: 'Fundamentals',
  Crowding: 'Crowding',
  Valuation: 'P/E premium to its 5-year average',
};

/** Unit appended to a condition's threshold and value, per axis. */
export const VERDICT_CONDITION_UNIT: Readonly<Record<VerdictAxis, string>> = {
  Structure: '',
  Fundamentals: '',
  Crowding: '',
  Valuation: '%',
};

export const VERDICT_OPERATOR_LABEL: Readonly<Record<VerdictOperator, string>> = {
  LessThan: '<',
  GreaterThan: '>',
  AtLeast: '≥',
  AtMost: '≤',
  Is: 'is',
  IsNot: 'is not',
  IsMissing: 'is missing',
  IsKnown: 'is known',
};

export const VERDICT_STRUCTURE_BASIS_NOTE: Readonly<Record<VerdictStructureBasis, string>> = {
  adjusted: 'Structure thresholds read the regime-adjusted score.',
  raw: 'No market regime reading, so structure thresholds read the raw score.',
};

/** Shown where an axis or a value has nothing to read. */
export const SCORECARD_MISSING = 'missing';
export const SCORECARD_NOT_SCORED = 'Not scored';
export const SCORECARD_UNKNOWN = 'Unknown';
export const SCORECARD_NOT_APPLICABLE = 'Not applicable: not a single equity';
export const SCORECARD_STALE_NOTE = 'Price or multiples are stale.';
export const SCORECARD_SCORE_OUT_OF = 100;

/** The tick beside a rule condition: met, or not met (including a missing axis). */
export const VERDICT_CONDITION_MET_CLASS = 'bg-gain';
export const VERDICT_CONDITION_UNMET_CLASS = 'bg-border-strong';
