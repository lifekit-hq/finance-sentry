import {ChangeDetectionStrategy, Component, computed, input} from '@angular/core';
import {TagComponent} from '@lifekit-hq/ui';

import {
  VERDICT_CONDITION_MET_CLASS,
  VERDICT_CONDITION_UNMET_CLASS,
  VERDICT_STRUCTURE_BASIS_NOTE,
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../constants/scorecard/scorecard.constants';
import {type ScorecardDto} from '../../models/scorecard/scorecard.model';
import {ScorecardUtils} from '../../utils/scorecard.utils';

/**
 * A scorecard's verdict word (spec 726 US2) and its drill-down: the four axes with the facts behind
 * each, and the whole ordered rule table with the rule that fired, every condition's threshold and the
 * value it read. The word is a research verdict, never an order: nothing here places a trade.
 */
@Component({
  selector: 'fns-scorecard-verdict',
  imports: [TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './scorecard-verdict.component.html',
})
export class ScorecardVerdictComponent {
  public readonly scorecard = input.required<ScorecardDto>();

  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;
  public readonly basisNote = VERDICT_STRUCTURE_BASIS_NOTE;
  public readonly metClass = VERDICT_CONDITION_MET_CLASS;
  public readonly unmetClass = VERDICT_CONDITION_UNMET_CLASS;

  public readonly axes = computed(() => ScorecardUtils.axes(this.scorecard()));
  public readonly rules = computed(() => ScorecardUtils.rules(this.scorecard().verdict));
  public readonly firedRule = computed(() => ScorecardUtils.firedRule(this.scorecard().verdict));
}
