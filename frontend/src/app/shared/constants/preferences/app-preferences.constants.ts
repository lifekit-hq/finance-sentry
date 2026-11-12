import {type SegmentedOption} from '@lifekit-hq/ui';

import {type VerdictDisplay} from '../../models/preferences/app-preferences.model';

export const VERDICT_DISPLAYS: readonly VerdictDisplay[] = ['simple', 'detailed'];

/** What a person sees before choosing, and when the account has not loaded. */
export const DEFAULT_VERDICT_DISPLAY: VerdictDisplay = 'simple';

export const VERDICT_DISPLAY_OPTIONS: readonly SegmentedOption[] = [
  {value: 'simple', label: 'Simple'},
  {value: 'detailed', label: 'Detailed'},
];

export const VERDICT_DISPLAY_LABEL = 'Verdict display';
