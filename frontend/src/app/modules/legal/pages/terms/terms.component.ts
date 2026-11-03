import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router} from '@angular/router';
import {AlertComponent, ButtonComponent} from '@lifekit-hq/ui';

import {
  LEGAL_TEMPLATE_NOTICE,
  NOT_ADVICE_NOTICE,
} from '../../../../shared/constants/legal/legal.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../../auth/store/auth.store';

/**
 * The terms of use for the owner's own instance, with the not-financial-advice and no-warranty clauses. Readable
 * before sign-in. The text is a template the owner reviews.
 */
@Component({
  selector: 'fns-terms',
  imports: [AlertComponent, ButtonComponent],
  templateUrl: './terms.component.html',
  host: {class: 'block h-full overflow-y-auto'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TermsComponent {
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);

  public readonly templateNotice = LEGAL_TEMPLATE_NOTICE;
  public readonly notAdviceNotice = NOT_ADVICE_NOTICE;
  public readonly backLabel = computed(() =>
    this.authStore.isAuthenticated() ? 'Back to settings' : 'Back to sign in'
  );

  public back(): void {
    void this.router.navigateByUrl(
      this.authStore.isAuthenticated() ? AppRoute.Settings : AppRoute.Login
    );
  }

  public openPrivacy(): void {
    void this.router.navigateByUrl(AppRoute.Privacy);
  }
}
