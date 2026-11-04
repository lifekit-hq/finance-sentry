import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router} from '@angular/router';
import {AlertComponent, ButtonComponent} from '@lifekit-hq/ui';

import {LEGAL_TEMPLATE_NOTICE} from '../../../../shared/constants/legal/legal.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../../auth/store/auth.store';

/**
 * The privacy notice for the owner's own instance (not for other copies of the open-source software). Readable before
 * sign-in; the login page and Settings link to it. The text is a template the owner reviews.
 */
@Component({
  selector: 'fns-privacy',
  imports: [AlertComponent, ButtonComponent],
  templateUrl: './privacy.component.html',
  host: {class: 'block h-full overflow-y-auto'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrivacyComponent {
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);

  public readonly templateNotice = LEGAL_TEMPLATE_NOTICE;
  public readonly backLabel = computed(() =>
    this.authStore.isAuthenticated() ? 'Back to settings' : 'Back to sign in'
  );

  public back(): void {
    void this.router.navigateByUrl(
      this.authStore.isAuthenticated() ? AppRoute.Settings : AppRoute.Login
    );
  }

  public openTerms(): void {
    void this.router.navigateByUrl(AppRoute.Terms);
  }
}
