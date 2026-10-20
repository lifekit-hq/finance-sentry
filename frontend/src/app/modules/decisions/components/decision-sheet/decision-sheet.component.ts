import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {FormControl, FormGroup, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  CMN_DRAWER_DATA,
  FormFieldComponent,
  type LucideIconName,
  TagComponent,
  TextareaComponent,
} from '@lifekit-hq/ui';

import {type DecisionSheetData} from '../../../../shared/models/recommendation/recommendation.model';
import {
  DECISION_NOTE_MAX_LENGTH,
  DECISION_TEXT_ROWS,
  HOLD_NO_ORDER_TEXT,
  NO_TRADE_WALL_TEXT,
  NO_TRADE_WALL_TITLE,
  RECOMMENDATION_ACTION_LABEL,
  RECOMMENDATION_ACTION_VARIANT,
} from '../../constants/recommendation/recommendation.constants';
import {OrderLinesPipe, RecommendationSizePipe} from '../../pipes/recommendation.pipe';
import {DecisionSheetStore} from '../../store/decision-sheet/decision-sheet.store';

/**
 * The Confirm or Reject sheet for one proposal. Confirm shows the exact order and the no-trade wall:
 * Finance Sentry records the decision and never places, stages or sends the order.
 */
@Component({
  selector: 'fns-decision-sheet',
  imports: [
    AlertComponent,
    ButtonComponent,
    FormFieldComponent,
    OrderLinesPipe,
    ReactiveFormsModule,
    RecommendationSizePipe,
    TagComponent,
    TextareaComponent,
  ],
  providers: [DecisionSheetStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './decision-sheet.component.html',
})
export class DecisionSheetComponent {
  private readonly data = inject<DecisionSheetData>(CMN_DRAWER_DATA);

  public readonly store = inject(DecisionSheetStore);
  public readonly recommendation = this.data.recommendation;
  public readonly isConfirm = this.data.verdict === 'confirm';
  public readonly actionLabel = RECOMMENDATION_ACTION_LABEL;
  public readonly actionVariant = RECOMMENDATION_ACTION_VARIANT;
  public readonly wallTitle = NO_TRADE_WALL_TITLE;
  public readonly wallText = NO_TRADE_WALL_TEXT;
  public readonly holdNoOrder = HOLD_NO_ORDER_TEXT;
  public readonly maxLength = DECISION_NOTE_MAX_LENGTH;
  public readonly rows = DECISION_TEXT_ROWS;
  public readonly copyIcon: LucideIconName = 'Copy';

  public readonly text = new FormControl('', {nonNullable: true});
  public readonly form = new FormGroup({text: this.text});
  public readonly typed = toSignal(this.text.valueChanges, {initialValue: ''});

  /** A rejection needs its real reason (kept as the counterfactual); a confirmation's note is optional. */
  public readonly canSend = computed(
    () => !this.store.isLoading() && (this.isConfirm || this.typed().trim() !== '')
  );

  public send(): void {
    if (!this.canSend()) {
      return;
    }
    this.store.submit(this.text.value);
  }
}
