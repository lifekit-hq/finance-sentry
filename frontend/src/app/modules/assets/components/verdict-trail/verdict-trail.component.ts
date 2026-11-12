import {ChangeDetectionStrategy, Component, computed, input} from '@angular/core';
import {TagComponent} from '@lifekit-hq/ui';

import {
  VERDICT_CONDITION_MET_CLASS,
  VERDICT_CONDITION_UNMET_CLASS,
  VERDICT_STRUCTURE_BASIS_NOTE,
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../../../shared/constants/scorecard/scorecard.constants';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';
import {ScorecardUtils} from '../../../../shared/utils/scorecard.utils';
import {
  VERDICT_TRAIL_FIRED_LABEL,
  VERDICT_TRAIL_INTRO,
  VERDICT_TRAIL_MISS_LABEL,
} from '../../constants/dossier/dossier-verdict.constants';

/**
 * The detailed reading of a verdict: the rules in the order the backend tries them, numbered, each with the
 * values its conditions read, down to the rule that fired. It restates the rule table the verdict already
 * carries (spec 726 US2); no step, bar or value is computed here.
 */
@Component({
  selector: 'fns-verdict-trail',
  imports: [TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './verdict-trail.component.html',
})
export class VerdictTrailComponent {
  public readonly scorecard = input.required<ScorecardDto>();

  public readonly intro = VERDICT_TRAIL_INTRO;
  public readonly firedLabel = VERDICT_TRAIL_FIRED_LABEL;
  public readonly missLabel = VERDICT_TRAIL_MISS_LABEL;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;
  public readonly basisNote = VERDICT_STRUCTURE_BASIS_NOTE;
  public readonly metClass = VERDICT_CONDITION_MET_CLASS;
  public readonly unmetClass = VERDICT_CONDITION_UNMET_CLASS;

  public readonly verdict = computed(() => this.scorecard().verdict);
  public readonly steps = computed(() => ScorecardUtils.trail(this.verdict()));
}
