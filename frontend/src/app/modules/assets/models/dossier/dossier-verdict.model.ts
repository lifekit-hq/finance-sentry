import {type Nullable} from '@lifekit-hq/ui';

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
