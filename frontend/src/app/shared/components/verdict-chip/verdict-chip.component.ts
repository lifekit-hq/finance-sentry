import {ChangeDetectionStrategy, Component, computed, inject, input} from '@angular/core';
import {type Nullable, TagComponent, ToastService, TooltipDirective} from '@lifekit-hq/ui';

import {NOT_ADVICE_NOTICE} from '../../constants/legal/legal.constants';
import {
  SCORECARD_GAP_COPY,
  SCORECARD_GAP_DASH,
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../constants/scorecard/scorecard.constants';
import {type ScorecardGapReason, type VerdictWord} from '../../models/scorecard/scorecard.model';

/**
 * A verdict word as a small chip on a position row (spec 726 US2): the same labels and colours as the
 * dossier's verdict. It is a label, not a control: the row it sits in already links to the dossier.
 *
 * Where the verdict could not be given the chip is a dash that says why (issue #1414): on hover, on keyboard
 * focus and on a long press through the tooltip, and on a tap as a toast (the tap does not open the row).
 */
@Component({
  selector: 'fns-verdict-chip',
  imports: [TagComponent, TooltipDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'inline-flex'},
  templateUrl: './verdict-chip.component.html',
})
export class VerdictChipComponent {
  private readonly toast = inject(ToastService);

  /** The word to show; null with a `gap` shows the dash instead. */
  public readonly word = input<Nullable<VerdictWord>>(null);
  /** Why there is no word, when there is none. */
  public readonly gap = input<Nullable<ScorecardGapReason>>(null);

  public readonly notAdvice = NOT_ADVICE_NOTICE;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;
  public readonly dash = SCORECARD_GAP_DASH;

  /** The reason as the chip words it: "No verdict for your role. Research verdicts are shown to the account owner." */
  public readonly gapText = computed((): Nullable<string> => {
    const gap = this.gap();
    return gap === null ? null : `${SCORECARD_GAP_COPY[gap].line} ${SCORECARD_GAP_COPY[gap].hint}`;
  });

  /** A tap says the reason and stays on the page, instead of opening the row's dossier. */
  public explain(event: Event): void {
    const text = this.gapText();
    if (text === null) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.toast.info(text);
  }
}
