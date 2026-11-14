import {ChangeDetectionStrategy, Component, computed, input} from '@angular/core';
import {TagComponent} from '@lifekit-hq/ui';

import {ScorecardVerdictComponent} from '../../../../shared/components/scorecard-verdict/scorecard-verdict.component';
import {NOT_ADVICE_NOTICE} from '../../../../shared/constants/legal/legal.constants';
import {
  VERDICT_FOR_NEW_MONEY,
  VERDICT_HELD_LINE,
  VERDICT_TURNS_BUY_PREFIX,
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../../../shared/constants/scorecard/scorecard.constants';
import {type VerdictDisplay} from '../../../../shared/models/preferences/app-preferences.model';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';
import {
  THESIS_MISSING_PROMPT,
  THESIS_STATE_VARIANT,
  VERDICT_REASON_MARK_CLASS,
  VERDICT_REASON_STATUS_LABEL,
} from '../../constants/dossier/dossier-verdict.constants';
import {type ThesisDto} from '../../models/dossier/dossier.model';
import {DossierVerdictUtils} from '../../utils/dossier-verdict.utils';
import {VerdictTrailComponent} from '../verdict-trail/verdict-trail.component';

/**
 * The per-share verdict (spec 726 US2): one block with the word labelled "for new money", the reasons behind
 * it, what would turn it into BUY, and the thesis with its kill-switch beside it. On a held position a fixed
 * line says holding follows the thesis and kill-switch, never the word. The reasons come from the backend's
 * rule table, so they cannot disagree with the word; nothing here places a trade. In the detailed display the
 * reasons give way to the numbered trail of rules that produced the word.
 */
@Component({
  selector: 'fns-verdict-block',
  imports: [ScorecardVerdictComponent, TagComponent, VerdictTrailComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './verdict-block.component.html',
})
export class VerdictBlockComponent {
  public readonly scorecard = input.required<ScorecardDto>();
  /** True when the book holds this name. */
  public readonly held = input(false);
  public readonly thesis = input<ThesisDto | null>(null);
  /** The owner's choice: the word with its reasons, or the numbered trail of rules behind it. */
  public readonly display = input<VerdictDisplay>('simple');

  public readonly notAdvice = NOT_ADVICE_NOTICE;
  public readonly forNewMoney = VERDICT_FOR_NEW_MONEY;
  public readonly heldLine = VERDICT_HELD_LINE;
  public readonly turnsBuyPrefix = VERDICT_TURNS_BUY_PREFIX;
  public readonly thesisMissingPrompt = THESIS_MISSING_PROMPT;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;
  public readonly thesisVariant = THESIS_STATE_VARIANT;
  public readonly reasonMark = VERDICT_REASON_MARK_CLASS;
  public readonly reasonStatus = VERDICT_REASON_STATUS_LABEL;

  public readonly verdict = computed(() => this.scorecard().verdict);
  public readonly status = computed(() => DossierVerdictUtils.thesisStatus(this.thesis()));
}
