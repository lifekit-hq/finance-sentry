import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {ConnectStore} from '../../store/connect/connect.store';
import {IbkrConnectStore} from '../../store/ibkr-connect/ibkr-connect.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {IbkrFlexGuideComponent} from './ibkr-flex-guide.component';
import {OAUTH_GUIDE_STEPS} from './ibkr-flex-guide.constants';
import {FLEX_FRESHNESS_NOTE, FLEX_QUERY_ID_PATTERN} from './ibkr-form.constants';
import {IbkrOauthFormComponent} from './ibkr-oauth-form.component';

@Component({
  selector: 'fns-ibkr-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    DatePipe,
    DialogActionsComponent,
    FormFieldComponent,
    IbkrFlexGuideComponent,
    IbkrOauthFormComponent,
    InputComponent,
    ReactiveFormsModule,
  ],
  providers: [IbkrConnectStore],
  templateUrl: './ibkr-form.component.html',
})
export class IbkrFormComponent {
  private readonly strategy = inject(CONNECT_STRATEGY);

  public readonly store = inject(ConnectStore);
  public readonly ibkr = inject(IbkrConnectStore);

  public readonly freshnessNote = FLEX_FRESHNESS_NOTE;
  public readonly oauthGuideSteps = OAUTH_GUIDE_STEPS;

  public readonly form = new FormGroup({
    token: new FormControl<string>('', {nonNullable: true, validators: [Validators.required]}),
    queryId: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(FLEX_QUERY_ID_PATTERN)],
    }),
  });

  public readonly showsConnectError = computed(() => this.ibkr.path() === 'flex');

  /** Spaces creep in when a token is copied from a web page. */
  private flexRequest(): {token: string; queryId: string} {
    const value = this.form.getRawValue();
    return {token: value.token.replace(/\s+/g, ''), queryId: value.queryId.trim()};
  }

  public check(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.store.resetError();
    this.ibkr.setPath('flex');
    this.ibkr.validate(this.flexRequest());
  }

  public confirm(): void {
    if (this.form.invalid || !this.ibkr.hasPreview()) {
      return;
    }
    this.ibkr.setPath('flex');
    this.store.connect({
      strategy: this.strategy,
      payload: {kind: 'flex', payload: this.flexRequest()},
    });
  }

  public edit(): void {
    this.store.resetError();
    this.ibkr.resetValidation();
  }

  public back(): void {
    this.store.setModalStep('type-picker');
  }
}
