import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import {FormBuilder, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  FormFieldComponent,
  GoogleSignInButtonComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {environment} from '../../../../../environments/environment';
import {AuthStore} from '../../store/auth.store';
import {
  GOOGLE_BUTTON_BASE_CONFIG,
  GOOGLE_BUTTON_LOCALE,
  GOOGLE_BUTTON_MAX_WIDTH,
  GOOGLE_BUTTON_MIN_WIDTH,
} from './login.constants';

@Component({
  selector: 'fns-login',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonComponent,
    FormFieldComponent,
    GoogleSignInButtonComponent,
    InputComponent,
  ],
  templateUrl: './login.component.html',
  host: {class: 'block h-full'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent implements AfterViewInit {
  private readonly authStore = inject(AuthStore);

  public readonly form = inject(FormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  public readonly googleLocale = GOOGLE_BUTTON_LOCALE;
  public readonly googleClientId = environment.googleClientId;
  public readonly googleContainer = viewChild.required<ElementRef<HTMLElement>>('googleContainer');
  public readonly googleWidth = signal<number | null>(null);
  public readonly googleButtonConfig = computed(() => {
    const width = this.googleWidth();
    return width === null ? null : {...GOOGLE_BUTTON_BASE_CONFIG, width};
  });
  public readonly signInMethods = this.authStore.signInMethods;
  public readonly loading = this.authStore.isLoading;
  public readonly errorMessage = this.authStore.errorMessage;
  public readonly flashMessage = this.authStore.flashMessage;

  public ngAfterViewInit(): void {
    const width = Math.round(this.googleContainer().nativeElement.clientWidth);
    this.googleWidth.set(
      Math.min(GOOGLE_BUTTON_MAX_WIDTH, Math.max(GOOGLE_BUTTON_MIN_WIDTH, width))
    );
  }

  public onSubmit(): void {
    if (this.form.invalid) {
      return;
    }
    const {email, password} = this.form.value;
    this.authStore.login({email: email ?? '', password: password ?? ''});
  }

  public onOidcSignIn(): void {
    this.authStore.startOidcSignIn();
  }

  public onGoogleCredential(credential: string): void {
    this.authStore.verifyGoogleCredential(credential);
  }
}
