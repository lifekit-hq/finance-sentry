import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, input} from '@angular/core';
import {AlertComponent, TagComponent} from '@lifekit-hq/ui';

import {
  LOOK_THROUGH_TITLE,
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../constants/scorecard/scorecard.constants';
import {type ScorecardDto} from '../../models/scorecard/scorecard.model';
import {ScorecardUtils} from '../../utils/scorecard.utils';

/**
 * The basis of an ETF's verdict (issue #1324), shown beside the word: the top holdings the fund was read through, each
 * with the word the single-name rules gave it, the share of weight reading BUY or better, and where the holdings came
 * from. When the look-through is not the full top 10 (a holding could not be scored, or no holdings were found) the
 * backend's note says so in a warning, so a degraded word never reads as a complete one.
 */
@Component({
  selector: 'fns-look-through',
  imports: [AlertComponent, DatePipe, TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './look-through.component.html',
})
export class LookThroughComponent {
  public readonly scorecard = input.required<ScorecardDto>();

  public readonly title = LOOK_THROUGH_TITLE;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;

  public readonly view = computed(() => ScorecardUtils.lookThrough(this.scorecard()));
  public readonly asOf = computed(() => this.scorecard().lookThrough?.asOf ?? null);
}
