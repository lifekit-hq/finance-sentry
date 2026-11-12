/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type TagVariant} from '@lifekit-hq/ui';

import {
  type VerdictReasonStatus,
  type VerdictWord,
} from '../../../../shared/models/scorecard/scorecard.model';
import {type ThesisStatusState} from '../../models/dossier/dossier-verdict.model';

/** Shown where a held name has no thesis, or an unwritten one has nothing for the kill-switch to watch. */
export const THESIS_MISSING_LABEL = 'No thesis on file';
export const THESIS_MISSING_PROMPT =
  'Write a thesis for this name so the kill-switch has something to watch. Until then only the word speaks.';
export const THESIS_ACTIVE_LABEL = 'Thesis active';
export const THESIS_BROKEN_LABEL = 'Thesis invalidated';
export const KILL_SWITCH_NONE_LABEL = 'No kill-switch rules';
export const KILL_SWITCH_TRIPPED_LABEL = 'Kill-switch tripped';

/** Colour of the thesis and kill-switch tags: green while intact, red once tripped, quiet when absent. */
export const THESIS_STATE_VARIANT: Readonly<Record<ThesisStatusState, TagVariant>> = {
  missing: 'neutral',
  active: 'success',
  tripped: 'error',
};

/**
 * The verdict words as the header's large status word reads them: the same vocabulary as the verdict
 * block's tag (`VERDICT_WORD_LABEL`), in sentence case because a 42px word in capitals shouts.
 */
export const VERDICT_HEADER_WORD_LABEL: Readonly<Record<VerdictWord, string>> = {
  StrongBuy: 'Strong buy',
  Buy: 'Buy',
  Wait: 'Wait',
  Avoid: 'Avoid',
  NotScored: 'Not scored',
};

/** The pill that keeps the word honest: it reads a position the owner does not yet hold. */
export const VERDICT_HEADER_NEW_MONEY_PILL = 'For new money';

/** The tick beside a reason: clears its bar, falls short, or has nothing to read. */
export const VERDICT_REASON_MARK_CLASS: Readonly<Record<VerdictReasonStatus, string>> = {
  Pass: 'bg-gain',
  Fail: 'bg-loss',
  Missing: 'bg-border-strong',
  NotApplicable: 'bg-border-strong',
};

/** Spoken to a screen reader beside each tick, since the colour alone says nothing. */
export const VERDICT_REASON_STATUS_LABEL: Readonly<Record<VerdictReasonStatus, string>> = {
  Pass: 'Clears the bar',
  Fail: 'Falls short',
  Missing: 'Nothing to read',
  NotApplicable: 'Does not apply',
};

/** The detailed reading's lead-in, and what each step says of itself. */
export const VERDICT_TRAIL_INTRO =
  'The rules are tried in this order, and the first one that matches decides.';
export const VERDICT_TRAIL_FIRED_LABEL = 'Matches: this decides the word';
export const VERDICT_TRAIL_MISS_LABEL = 'Does not match';
