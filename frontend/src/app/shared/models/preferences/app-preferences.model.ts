import {type AccountPreferences} from '@lifekit-hq/core/preferences';

/** How a research verdict is read: the word with its reasons, or the numbered trail of rules behind it. */
export type VerdictDisplay = 'simple' | 'detailed';

/** The account tier of this app: the kit's appearance plus the fields Finance Sentry adds. */
export interface AppPreferences extends AccountPreferences {
  verdictDisplay: VerdictDisplay;
}
