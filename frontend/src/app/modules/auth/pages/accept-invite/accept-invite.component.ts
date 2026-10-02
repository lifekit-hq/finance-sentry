import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormBuilder, ReactiveFormsModule, Validators} from '@angular/forms';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {AlertComponent, ButtonComponent, FormFieldComponent, InputComponent} from '@lifekit-hq/ui';

import {
  ACCEPT_INVITE_TOKEN_PARAM,
  ACCEPT_INVITE_USER_PARAM,
  AppRoute,
} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../store/auth.store';
import {passwordsMatch} from '../../validators/password-match.validator';

const MIN_PASSWORD_LENGTH = 8;

@Component({
  selector: 'fns-accept-invite',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonComponent,
    FormFieldComponent,
    InputComponent,
  ],
  templateUrl: './accept-invite.component.html',
  host: {class: 'block h-full'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AcceptInviteComponent {
  private readonly authStore = inject(AuthStore);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  private readonly userId = this.query.get(ACCEPT_INVITE_USER_PARAM) ?? '';
  private readonly token = this.query.get(ACCEPT_INVITE_TOKEN_PARAM) ?? '';

  public readonly form = inject(FormBuilder).group(
    {
      password: ['', [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH)]],
      confirmPassword: ['', Validators.required],
    },
    {validators: passwordsMatch}
  );
  public readonly linkComplete = this.userId !== '' && this.token !== '';
  public readonly loginRoute = AppRoute.Login;
  public readonly loading = this.authStore.isLoading;
  public readonly errorMessage = this.authStore.errorMessage;

  public onSubmit(): void {
    if (!this.linkComplete || this.form.invalid) {
      return;
    }
    this.authStore.acceptInvite({
      userId: this.userId,
      token: this.token,
      password: this.form.value.password ?? '',
    });
  }
}
