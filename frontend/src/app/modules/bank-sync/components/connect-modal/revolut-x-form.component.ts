import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {REVOLUT_X_HELP_URL} from './connect-modal.constants';

@Component({
  selector: 'fns-revolut-x-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
  ],
  templateUrl: './revolut-x-form.component.html',
})
export class RevolutXFormComponent {
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly accountsStore = inject(AccountsStore);

  public readonly store = inject(ConnectStore);

  public readonly form = new FormGroup({
    apiKey: new FormControl<string>('', {nonNullable: true, validators: [Validators.required]}),
    privateKey: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  public readonly helpUrl = REVOLUT_X_HELP_URL;

  public readonly isDuplicateError = computed(() => this.store.errorCode() === 'ALREADY_CONNECTED');

  public submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const {apiKey, privateKey} = this.form.getRawValue();
    this.store.connect({
      strategy: this.strategy,
      payload: {apiKey: apiKey.trim(), privateKey: privateKey.trim()},
    });
  }

  public back(): void {
    this.store.setModalStep('provider-picker');
  }

  public disconnectExisting(): void {
    this.accountsStore.disconnectRevolutX();
    this.store.resetError();
    this.form.reset({apiKey: '', privateKey: ''});
  }
}
