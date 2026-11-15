/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type TagVariant} from '@lifekit-hq/ui';

import {
  type ThesisRevisionChangeField,
  type ThesisRevisionKind,
  type ThesisRevisionStatus,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';

export const THESIS_REVISION_KIND_LABEL: Readonly<Record<ThesisRevisionKind, string>> = {
  Promote: 'Promote to thesis',
  NarrativeUpdate: 'Update thesis',
  TriggerChange: 'Change triggers',
  Close: 'Close thesis',
};

export const THESIS_REVISION_KIND_VARIANT: Readonly<Record<ThesisRevisionKind, TagVariant>> = {
  Promote: 'info',
  NarrativeUpdate: 'neutral',
  TriggerChange: 'neutral',
  Close: 'warning',
};

export const THESIS_REVISION_STATUS_LABEL: Readonly<Record<ThesisRevisionStatus, string>> = {
  Pending: 'Pending',
  Confirmed: 'Confirmed',
  Rejected: 'Rejected',
  Expired: 'Expired',
  Superseded: 'Superseded',
};

export const THESIS_REVISION_STATUS_VARIANT: Readonly<Record<ThesisRevisionStatus, TagVariant>> = {
  Pending: 'info',
  Confirmed: 'success',
  Rejected: 'neutral',
  Expired: 'neutral',
  Superseded: 'neutral',
};

export const THESIS_REVISION_CHANGE_LABEL: Readonly<Record<ThesisRevisionChangeField, string>> = {
  text: 'Thesis text',
  triggers: 'Invalidation triggers',
  dataPoints: 'Key data points',
  catalysts: 'Catalysts',
  falsifiers: 'Narrative falsifiers',
  entryPrice: 'Entry price',
};

/** The no-position wall: confirming writes the thesis only. */
export const THESIS_REVISION_WALL_TITLE = 'This changes your thesis, not your position.';
export const THESIS_REVISION_WALL_TEXT =
  'Confirming writes the thesis and journals the change. Finance Sentry never places or changes a position.';

export const THESIS_REVISION_PROMOTE_DEFAULTS_TEXT =
  "No text or triggers proposed: the promotion uses the scorecard's text and the standard trigger set.";
export const THESIS_REVISION_CLOSE_TEXT =
  'Ledger proposes retiring this thesis. Its monitoring stops; the journal keeps the history.';
export const THESIS_REVISION_NO_BEFORE_TEXT = 'No thesis yet';
export const THESIS_REVISION_UNCHANGED_TEXT = 'Unchanged';

export const THESIS_REVISION_NOTE_MAX_LENGTH = 2000;
export const THESIS_REVISION_TEXT_ROWS = 3;
export const THESIS_REVISION_SHEET_WIDTH = '28rem';
export const THESIS_REVISION_FAILED_MESSAGE = 'Could not record the decision. Try again.';
export const THESIS_REVISIONS_LOAD_FAILED_MESSAGE = 'Could not load the thesis changes.';
export const THESIS_REVISION_CONFIRMED_TOAST = 'Thesis updated.';
export const THESIS_REVISION_REJECTED_TOAST = 'Rejection recorded.';
export const THESIS_REVISION_OVERRIDE_LABEL = 'Confirm anyway, overriding the risk gate';
export const THESIS_REVISION_OVERRIDE_HINT =
  'The risk gate refused this promotion. Overriding is your call alone and is journaled.';

/** Rows the loading placeholder draws for the thesis-change cards. */
export const THESIS_REVISION_SKELETON_ROWS = 2;

export const THESIS_REVISIONS_EMPTY_COPY = {
  message: 'No thesis changes yet',
  subMessage:
    'Ledger proposes a promotion or a revision here when the evidence calls for one. Only you confirm it.',
} as const;

/** The dossier's note once its thesis change is decided; the card's place says what was recorded. */
export const THESIS_REVISION_RECORDED_TITLE: Readonly<Record<ThesisRevisionStatus, string>> = {
  Pending: 'Thesis change pending',
  Confirmed: 'Thesis change confirmed',
  Rejected: 'Thesis change rejected',
  Expired: 'Thesis change expired',
  Superseded: 'Thesis change superseded',
};
