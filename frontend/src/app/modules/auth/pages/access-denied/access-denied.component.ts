import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {ButtonComponent, PageComponent} from '@lifekit-hq/ui';

import {AuthStore} from '../../store/auth.store';

/**
 * Where the sign-in callback sends a person the identity provider knows but the app does not: no account here and
 * no app role on the provider, or an email the provider has not verified.
 */
@Component({
  selector: 'fns-access-denied',
  imports: [ButtonComponent, PageComponent],
  templateUrl: './access-denied.component.html',
  host: {class: 'block'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessDeniedComponent {
  private readonly authStore = inject(AuthStore);

  public onTryAgain(): void {
    this.authStore.startOidcSignIn();
  }
}
