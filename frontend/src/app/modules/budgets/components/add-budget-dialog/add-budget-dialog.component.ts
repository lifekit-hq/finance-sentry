import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
  SelectComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {MIN_BUDGET_LIMIT} from '../../constants/budget/budget.constants';
import {type CreateBudgetRequest} from '../../models/budget/budget.model';

@Component({
  selector: 'fns-add-budget-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InputHintsDirective,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
    SelectComponent,
  ],
  templateUrl: './add-budget-dialog.component.html',
})
export class AddBudgetDialogComponent {
  private readonly dialogRef = inject<DialogRef<CreateBudgetRequest>>(DialogRef);
  private readonly categoryStore = inject(CategoryStore);

  public readonly categoryOptions = computed(() =>
    this.categoryStore.categories().map(c => ({value: c.key, label: c.label}))
  );

  public readonly form = new FormGroup({
    category: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    monthlyLimit: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(MIN_BUDGET_LIMIT)],
    }),
  });

  public submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({category: value.category, monthlyLimit: Number(value.monthlyLimit)});
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
