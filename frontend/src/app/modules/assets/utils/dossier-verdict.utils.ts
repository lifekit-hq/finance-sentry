import {type Nullable} from '@lifekit-hq/ui';

import {
  VERDICT_HELD_LINE,
  VERDICT_TURNS_BUY_PREFIX,
  VERDICT_WORD_VARIANT,
} from '../../../shared/constants/scorecard/scorecard.constants';
import {type ScorecardDto} from '../../../shared/models/scorecard/scorecard.model';
import {
  KILL_SWITCH_NONE_LABEL,
  KILL_SWITCH_TRIPPED_LABEL,
  THESIS_ACTIVE_LABEL,
  THESIS_BROKEN_LABEL,
  THESIS_MISSING_LABEL,
  THESIS_STATE_VARIANT,
  VERDICT_HEADER_NEW_MONEY_PILL,
  VERDICT_HEADER_WORD_LABEL,
} from '../constants/dossier/dossier-verdict.constants';
import {type ThesisDto} from '../models/dossier/dossier.model';
import {
  type ThesisStatusView,
  type VerdictHeaderView,
} from '../models/dossier/dossier-verdict.model';
import {ThesisTriggerUtils} from './thesis-trigger.utils';

/** Display helpers for the verdict block's thesis / kill-switch side. */
export class DossierVerdictUtils {
  /**
   * The thesis and kill-switch status: missing (no thesis), tripped (invalidated, with the reason), or active
   * (with the count of kill rules, or the plain note that none are set).
   */
  public static thesisStatus(thesis: Nullable<ThesisDto>): ThesisStatusView {
    if (!thesis) {
      return {
        state: 'missing',
        thesisLabel: THESIS_MISSING_LABEL,
        killSwitchLabel: null,
        rules: [],
        brokenReason: null,
      };
    }
    const rules = thesis.invalidationTriggers.map(t => ThesisTriggerUtils.describe(t));
    if (thesis.brokenAt) {
      return {
        state: 'tripped',
        thesisLabel: THESIS_BROKEN_LABEL,
        killSwitchLabel: KILL_SWITCH_TRIPPED_LABEL,
        rules,
        brokenReason: thesis.brokenReason,
      };
    }
    return {
      state: 'active',
      thesisLabel: THESIS_ACTIVE_LABEL,
      killSwitchLabel:
        rules.length === 0
          ? KILL_SWITCH_NONE_LABEL
          : `Kill-switch armed: ${rules.length} ${rules.length === 1 ? 'rule' : 'rules'}`,
      rules,
      brokenReason: null,
    };
  }

  /**
   * The header's read of the scorecard the page already carries: the verdict word, the pills that keep it
   * honest ("for new money", the thesis state) and one caveat. Nothing is computed here; a name without a
   * scorecard has no verdict, so it has no header.
   */
  public static header(
    scorecard: Nullable<ScorecardDto>,
    held: boolean,
    thesis: Nullable<ThesisDto>
  ): Nullable<VerdictHeaderView> {
    if (!scorecard) {
      return null;
    }
    const {word, turnsBuyWhen} = scorecard.verdict;
    const thesisState = DossierVerdictUtils.thesisStatus(thesis);
    return {
      status: {tone: VERDICT_WORD_VARIANT[word], label: VERDICT_HEADER_WORD_LABEL[word]},
      pills: [
        {label: VERDICT_HEADER_NEW_MONEY_PILL, tone: 'neutral'},
        {label: thesisState.thesisLabel, tone: THESIS_STATE_VARIANT[thesisState.state]},
      ],
      // A held name carries the fixed line (the word never drives a sale or an add); otherwise what turns it BUY.
      caveat: held
        ? VERDICT_HELD_LINE
        : turnsBuyWhen
          ? `${VERDICT_TURNS_BUY_PREFIX} ${turnsBuyWhen}.`
          : null,
    };
  }
}
