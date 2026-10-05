import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {AlertComponent, ButtonComponent, FormFieldComponent, InputComponent} from '@lifekit-hq/ui';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {IbkrConnectStore} from '../../store/ibkr-connect/ibkr-connect.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {CONSUMER_KEY_LENGTH, type PemControlName} from './ibkr-form.constants';

@Component({
  selector: 'fns-ibkr-oauth-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
  ],
  templateUrl: './ibkr-oauth-form.component.html',
})
export class IbkrOauthFormComponent {
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly accountsStore = inject(AccountsStore);

  private readonly ibkrStore = inject(IbkrConnectStore);

  public readonly store = inject(ConnectStore);

  public readonly form = new FormGroup({
    consumerKey: new FormControl<string>('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.minLength(CONSUMER_KEY_LENGTH),
        Validators.maxLength(CONSUMER_KEY_LENGTH),
      ],
    }),
    accessToken: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    accessTokenSecret: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    signatureKey: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    encryptionKey: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    dhParam: new FormControl<string>('', {nonNullable: true, validators: [Validators.required]}),
  });

  /** The shared connect error belongs here only when this path made the last attempt. */
  public readonly showsError = computed(() => this.ibkrStore.path() === 'oauth');
  public readonly isDuplicateError = computed(
    () => this.showsError() && this.store.errorCode() === 'IBKR_DUPLICATE'
  );

  public async onFileSelected(event: Event, control: PemControlName): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }
    const text = await file.text();
    this.form.controls[control].setValue(text.trim());
    this.form.controls[control].markAsTouched();
  }

  public submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.ibkrStore.setPath('oauth');
    this.store.connect({
      strategy: this.strategy,
      payload: {
        kind: 'oauth',
        payload: {
          consumerKey: value.consumerKey.trim().toUpperCase(),
          accessToken: value.accessToken.trim(),
          accessTokenSecret: value.accessTokenSecret.trim(),
          signatureKey: value.signatureKey.trim(),
          encryptionKey: value.encryptionKey.trim(),
          dhParam: value.dhParam.trim(),
        },
      },
    });
  }

  public disconnectExisting(): void {
    this.accountsStore.disconnectIBKR();
    this.store.resetError();
    this.form.reset({
      consumerKey: '',
      accessToken: '',
      accessTokenSecret: '',
      signatureKey: '',
      encryptionKey: '',
      dhParam: '',
    });
  }
}
