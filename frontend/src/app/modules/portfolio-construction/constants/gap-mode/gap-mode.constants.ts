/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type TagVariant} from '@lifekit-hq/ui';

import {type GapStatus} from '../../models/gap-mode/gap-mode.model';

export const GAP_STATUS_LABEL: Readonly<Record<GapStatus, string>> = {
  Missing: 'Missing',
  Underweight: 'Underweight',
  OnTarget: 'On target',
  Overweight: 'Overweight',
};

export const GAP_STATUS_VARIANT: Readonly<Record<GapStatus, TagVariant>> = {
  Missing: 'warning',
  Underweight: 'warning',
  OnTarget: 'neutral',
  Overweight: 'info',
};

export const GAP_TITLE = 'Your book against this target';
export const GAP_LOAD_ERROR = 'Failed to load how your book compares.';
export const GAP_HELD_OUTSIDE_LABEL = 'Held, not in target';
export const GAP_NAMES_HEADING = 'Names';
export const GAP_SLEEVES_HEADING = 'Sleeves';
export const GAP_SECTORS_HEADING = 'Sectors';
export const GAP_NO_BOOK_COPY =
  'Nothing held to compare yet. The names below are the target in full.';
export const GAP_SKELETON_ROWS = 3;
