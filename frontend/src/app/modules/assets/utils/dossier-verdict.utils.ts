import {type Nullable} from '@lifekit-hq/ui';

import {
  KILL_SWITCH_NONE_LABEL,
  KILL_SWITCH_TRIPPED_LABEL,
  THESIS_ACTIVE_LABEL,
  THESIS_BROKEN_LABEL,
  THESIS_MISSING_LABEL,
} from '../constants/dossier/dossier-verdict.constants';
import {type ThesisDto} from '../models/dossier/dossier.model';
import {type ThesisStatusView} from '../models/dossier/dossier-verdict.model';
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
}
