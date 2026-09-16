import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
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
import {
  MONOBANK_TOKEN_MAX_LENGTH,
  MONOBANK_TOKEN_MIN_LENGTH,
  MONOBANK_TOKEN_PATTERN,
} from './connect-modal.constants';

@Component({
  selector: 'fns-monobank-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
  ],
  templateUrl: './monobank-form.component.html',
})
export class MonobankFormComponent {
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly accountsStore = inject(AccountsStore, {optional: true});

  private readonly token = new FormControl<string>('', {
    nonNullable: true,
    validators: [
      Validators.required,
      Validators.minLength(MONOBANK_TOKEN_MIN_LENGTH),
      Validators.maxLength(MONOBANK_TOKEN_MAX_LENGTH),
      Validators.pattern(MONOBANK_TOKEN_PATTERN),
    ],
  });

  // FormControl.touched/invalid are not signals — bridge via the control's
  // unified event stream so the computed re-evaluates on value/status/touched changes.
  private readonly tokenControlEvents = toSignal(this.token.events);

  public readonly store = inject(ConnectStore);

  public readonly form = new FormGroup({token: this.token});

  public readonly tokenControl = this.form.controls.token;

  public readonly tokenError = computed(() => {
    this.tokenControlEvents();
    return this.tokenControl.touched && this.tokenControl.invalid
      ? "This doesn't look like a Monobank token"
      : '';
  });

  public readonly isDuplicateError = computed(
    () => this.store.errorCode() === 'MONOBANK_TOKEN_DUPLICATE'
  );

  public onPaste(event: ClipboardEvent): void {
    const pasted = event.clipboardData?.getData('text');
    if (pasted === undefined) {
      return;
    }
    event.preventDefault();
    this.tokenControl.setValue(pasted.trim());
    this.tokenControl.markAsTouched();
  }

  public submit(): void {
    if (this.tokenControl.invalid) {
      this.tokenControl.markAsTouched();
      return;
    }
    this.store.connect({strategy: this.strategy, payload: {token: this.tokenControl.value.trim()}});
  }

  public back(): void {
    this.store.setModalStep('provider-picker');
  }

  public disconnectExisting(): void {
    this.accountsStore?.disconnectMonobank();
    this.store.resetError();
    this.tokenControl.reset('');
  }
}
