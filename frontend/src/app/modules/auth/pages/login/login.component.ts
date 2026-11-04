import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {AlertComponent, ButtonComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../store/auth.store';

@Component({
  selector: 'fns-login',
  imports: [AlertComponent, ButtonComponent, RouterLink, SkeletonComponent],
  templateUrl: './login.component.html',
  host: {class: 'block h-full'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly authStore = inject(AuthStore);

  public readonly signInMethods = this.authStore.signInMethods;
  public readonly methodsFailed = computed(() => this.authStore.signInMethodsStatus() === 'error');
  public readonly methodsPending = computed(
    () => this.authStore.signInMethods() === null && !this.methodsFailed()
  );
  /** The API answered and offers no way in: there is nothing to forward to or click. */
  public readonly noMethods = computed(() => {
    const methods = this.signInMethods();
    return methods !== null && !methods.oidc;
  });
  public readonly flashMessage = this.authStore.flashMessage;
  public readonly privacyRoute = AppRoute.Privacy;
  public readonly termsRoute = AppRoute.Terms;

  public retryMethods(): void {
    this.authStore.loadSignInMethods();
  }

  public onOidcSignIn(): void {
    this.authStore.startOidcSignIn();
  }
}
