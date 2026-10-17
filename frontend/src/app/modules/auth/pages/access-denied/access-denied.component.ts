import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {ButtonComponent} from '@lifekit-hq/ui';

import {AuthStore} from '../../store/auth.store';

/**
 * Where the sign-in callback sends a person the identity provider knows but the app does not: no account here and
 * no app role on the provider, or an email the provider has not verified.
 */
@Component({
  selector: 'fns-access-denied',
  imports: [ButtonComponent],
  templateUrl: './access-denied.component.html',
  host: {class: 'block h-full'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessDeniedComponent {
  private readonly authStore = inject(AuthStore);

  public onTryAgain(): void {
    this.authStore.startOidcSignIn();
  }
}
