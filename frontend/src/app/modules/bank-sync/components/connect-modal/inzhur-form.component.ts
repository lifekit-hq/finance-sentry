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

import {
  INZHUR_READ_ONLY_NOTE,
  INZHUR_SESSION_MAX_LENGTH,
  INZHUR_SESSION_NOTE,
  INZHUR_SESSION_STEPS,
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
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
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
  public readonly sessionNote = INZHUR_SESSION_NOTE;
  public readonly sessionSteps = INZHUR_SESSION_STEPS;

  public readonly sessionForm = new FormGroup({
    refreshToken: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(INZHUR_SESSION_MAX_LENGTH)],
    }),
  });

  /** Hands the pasted session to the backend, which proves it with one refresh. */
  public connect(): void {
    const refreshToken = this.sessionForm.getRawValue().refreshToken.trim();
    if (this.sessionForm.invalid || !refreshToken) {
      this.sessionForm.markAllAsTouched();
      return;
    }
    this.store.connect({strategy: this.strategy, payload: {refreshToken}});
  }

  public back(): void {
    this.store.setModalStep('provider-picker');
  }
}
