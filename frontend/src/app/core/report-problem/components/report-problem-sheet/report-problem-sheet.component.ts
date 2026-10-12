import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {FormControl, FormGroup, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  ChipComponent,
  FormFieldComponent,
  TextareaComponent,
} from '@lifekit-hq/ui';

import {
  REPORT_PROBLEM_HELPER,
  REPORT_PROBLEM_KINDS,
  REPORT_PROBLEM_TEXT_MAX_LENGTH,
  REPORT_PROBLEM_TEXT_ROWS,
} from '../../constants/report-problem.constants';
import {ReportProblemStore} from '../../store/report-problem.store';

/** The body of the "Report a problem" drawer: an optional kind, optional text, one send button. */
@Component({
  selector: 'fns-report-problem-sheet',
  imports: [
    AlertComponent,
    ButtonComponent,
    ChipComponent,
    FormFieldComponent,
    ReactiveFormsModule,
    TextareaComponent,
  ],
  providers: [ReportProblemStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-problem-sheet.component.html',
})
export class ReportProblemSheetComponent {
  public readonly store = inject(ReportProblemStore);

  public readonly kinds = REPORT_PROBLEM_KINDS;
  public readonly helper = REPORT_PROBLEM_HELPER;
  public readonly maxLength = REPORT_PROBLEM_TEXT_MAX_LENGTH;
  public readonly rows = REPORT_PROBLEM_TEXT_ROWS;
  public readonly text = new FormControl('', {nonNullable: true});
  public readonly form = new FormGroup({text: this.text});

  public readonly typed = toSignal(this.text.valueChanges, {initialValue: ''});

  /** Everything is optional, but an empty report would only spend one of the hourly sends. */
  public readonly canSend = computed(
    () => !this.store.isSubmitting() && (this.store.kind() !== null || this.typed().trim() !== '')
  );

  public send(): void {
    if (!this.canSend()) {
      return;
    }
    this.store.submit(this.text.value);
  }
}
