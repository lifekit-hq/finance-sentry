import {type Nullable, type VerdictPill, type VerdictStatus} from '@lifekit-hq/ui';

/** What the page hands `cmn-verdict-header`: the word, the facts beside it and the one line under it. */
export interface VerdictHeaderView {
  status: VerdictStatus;
  pills: VerdictPill[];
  /** Null when the verdict has no caveat to carry. */
  caveat: Nullable<string>;
}

export type ThesisStatusState = 'missing' | 'active' | 'tripped';

/** The thesis and its kill-switch as the verdict block shows them beside the word. */
export interface ThesisStatusView {
  state: ThesisStatusState;
  thesisLabel: string;
  /** Null while there is no thesis: a kill-switch belongs to one. */
  killSwitchLabel: Nullable<string>;
  /** The kill-switch rules as sentences (empty when none are set). */
  rules: string[];
  /** Why the thesis was invalidated, once it was. */
  brokenReason: Nullable<string>;
}
