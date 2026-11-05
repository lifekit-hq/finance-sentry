import {ChangeDetectionStrategy, Component, input} from '@angular/core';
import {TagComponent} from '@lifekit-hq/ui';

import {NOT_ADVICE_NOTICE} from '../../constants/legal/legal.constants';
import {
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../constants/scorecard/scorecard.constants';
import {type VerdictWord} from '../../models/scorecard/scorecard.model';

/**
 * A verdict word as a small chip on a position row (spec 726 US2): the same labels and colours as the
 * dossier's verdict. It is a label, not a control: the row it sits in already links to the dossier.
 */
@Component({
  selector: 'fns-verdict-chip',
  imports: [TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'inline-flex'},
  templateUrl: './verdict-chip.component.html',
})
export class VerdictChipComponent {
  public readonly word = input.required<VerdictWord>();

  public readonly notAdvice = NOT_ADVICE_NOTICE;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;
}
