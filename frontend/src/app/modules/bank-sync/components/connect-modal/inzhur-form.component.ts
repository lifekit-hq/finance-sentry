import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {
  INZHUR_CODE_PATTERN,
  INZHUR_PHONE_PATTERN,
  INZHUR_READ_ONLY_NOTE,
  INZHUR_SMS_NOTE,
} from '../../constants/inzhur/inzhur.constants';
import {ConnectStore} from '../../store/connect/connect.store';
import {InzhurConnectStore} from '../../store/inzhur-connect/inzhur-connect.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';

@Component({
  selector: 'fns-inzhur-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    DatePipe,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    InputHintsDirective,
    ReactiveFormsModule,
  ],
  providers: [InzhurConnectStore],
  templateUrl: './inzhur-form.component.html',
})
export class InzhurFormComponent {
  private readonly strategy = inject(CONNECT_STRATEGY);

  public readonly store = inject(ConnectStore);
  public readonly inzhur = inject(InzhurConnectStore);

  public readonly readOnlyNote = INZHUR_READ_ONLY_NOTE;
  public readonly smsNote = INZHUR_SMS_NOTE;

  public readonly credentialsForm = new FormGroup({
    phone: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(INZHUR_PHONE_PATTERN)],
    }),
    password: new FormControl<string>('', {nonNullable: true, validators: [Validators.required]}),
  });

  public readonly codeForm = new FormGroup({
    code: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(INZHUR_CODE_PATTERN)],
    }),
  });

  /** Starts the sign-in, which makes Inzhur text the owner a code. */
  public sendCode(): void {
    if (!this.inzhur.asksForCredentials()) {
      this.store.resetError();
      this.inzhur.start({phone: null, password: null});
      return;
    }
    if (this.credentialsForm.invalid) {
      this.credentialsForm.markAllAsTouched();
      return;
    }
    const {phone, password} = this.credentialsForm.getRawValue();
    this.store.resetError();
    this.inzhur.start({phone: phone.trim(), password});
  }

  public verify(): void {
    if (this.codeForm.invalid) {
      this.codeForm.markAllAsTouched();
      return;
    }
    this.store.connect({
      strategy: this.strategy,
      payload: {code: this.codeForm.getRawValue().code.replace(/\s+/g, '')},
    });
  }

  public startOver(): void {
    this.codeForm.reset();
    this.store.resetError();
    this.inzhur.backToCredentials();
  }

  public back(): void {
    this.store.setModalStep('provider-picker');
  }
}
