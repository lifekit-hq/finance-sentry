import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {FormBuilder, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  FormFieldComponent,
  InputComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AuthStore} from '../../store/auth.store';

@Component({
  selector: 'fns-login',
  imports: [
    InputHintsDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonComponent,
    FormFieldComponent,
    InputComponent,
    SkeletonComponent,
  ],
  templateUrl: './login.component.html',
  host: {class: 'block h-full'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly authStore = inject(AuthStore);

  public readonly form = inject(FormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  public readonly signInMethods = this.authStore.signInMethods;
  /** No form to show: the identity provider is the only way in, and this page forwards there. */
  public readonly oidcOnly = computed(() => {
    const methods = this.signInMethods();
    return methods !== null && methods.oidc && !methods.passwordLogin;
  });
  public readonly methodsFailed = computed(() => this.authStore.signInMethodsStatus() === 'error');
  public readonly methodsPending = computed(
    () => this.authStore.signInMethods() === null && !this.methodsFailed()
  );
  public readonly loading = this.authStore.isLoading;
  public readonly errorMessage = this.authStore.errorMessage;
  public readonly flashMessage = this.authStore.flashMessage;

  public onSubmit(): void {
    if (this.form.invalid) {
      return;
    }
    const {email, password} = this.form.value;
    this.authStore.login({email: email ?? '', password: password ?? ''});
  }

  public retryMethods(): void {
    this.authStore.loadSignInMethods();
  }

  public onOidcSignIn(): void {
    this.authStore.startOidcSignIn();
  }
}
