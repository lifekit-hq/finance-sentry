import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {FormControl, FormGroup, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  CheckboxComponent,
  CMN_DRAWER_DATA,
  FormFieldComponent,
  TagComponent,
  TextareaComponent,
} from '@lifekit-hq/ui';

import {type ThesisRevisionSheetData} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  THESIS_REVISION_KIND_LABEL,
  THESIS_REVISION_KIND_VARIANT,
  THESIS_REVISION_NOTE_MAX_LENGTH,
  THESIS_REVISION_OVERRIDE_HINT,
  THESIS_REVISION_OVERRIDE_LABEL,
  THESIS_REVISION_TEXT_ROWS,
  THESIS_REVISION_WALL_TEXT,
  THESIS_REVISION_WALL_TITLE,
} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisRevisionSheetStore} from '../../store/thesis-revision-sheet/thesis-revision-sheet.store';

/**
 * The Confirm or Reject sheet for one thesis change. Confirm writes the thesis and nothing else; a promotion the
 * risk gate refuses stays pending and offers the owner's explicit override. Reject needs the real reason.
 */
@Component({
  selector: 'fns-thesis-revision-sheet',
  imports: [
    AlertComponent,
    ButtonComponent,
    CheckboxComponent,
    FormFieldComponent,
    MoneyPipe,
    ReactiveFormsModule,
    TagComponent,
    TextareaComponent,
  ],
  providers: [ThesisRevisionSheetStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './thesis-revision-sheet.component.html',
})
export class ThesisRevisionSheetComponent {
  private readonly data = inject<ThesisRevisionSheetData>(CMN_DRAWER_DATA);

  public readonly store = inject(ThesisRevisionSheetStore);
  public readonly revision = this.data.revision;
  public readonly isConfirm = this.data.verdict === 'confirm';
  public readonly kindLabel = THESIS_REVISION_KIND_LABEL;
  public readonly kindVariant = THESIS_REVISION_KIND_VARIANT;
  public readonly wallTitle = THESIS_REVISION_WALL_TITLE;
  public readonly wallText = THESIS_REVISION_WALL_TEXT;
  public readonly overrideLabel = THESIS_REVISION_OVERRIDE_LABEL;
  public readonly overrideHint = THESIS_REVISION_OVERRIDE_HINT;
  public readonly maxLength = THESIS_REVISION_NOTE_MAX_LENGTH;
  public readonly rows = THESIS_REVISION_TEXT_ROWS;

  public readonly text = new FormControl('', {nonNullable: true});
  public readonly overrideRisk = new FormControl(false, {nonNullable: true});
  public readonly form = new FormGroup({text: this.text, overrideRisk: this.overrideRisk});
  public readonly typed = toSignal(this.text.valueChanges, {initialValue: ''});

  /** A rejection needs its real reason (kept as the counterfactual); a confirmation's note is optional. */
  public readonly canSend = computed(
    () => !this.store.isLoading() && (this.isConfirm || this.typed().trim() !== '')
  );

  public send(): void {
    if (!this.canSend()) {
      return;
    }
    this.store.submit({text: this.text.value, overrideRisk: this.overrideRisk.value});
  }
}
