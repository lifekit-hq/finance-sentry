import {ChangeDetectionStrategy, Component, effect, inject} from '@angular/core';
import {ActivatedRoute, Router} from '@angular/router';
import {CardComponent, PageHeaderComponent} from '@lifekit-hq/ui';

import {environment} from '../../../../../environments/environment';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../store/auth.store';

@Component({
  selector: 'fns-mcp-connect',
  imports: [CardComponent, PageHeaderComponent],
  template: `
    <div class="p-cmn-4 sm:p-cmn-6">
      <div class="mx-auto max-w-[36rem] space-y-cmn-8 pt-cmn-8">
        <cmn-page-header title="Connecting MCP" />
        <cmn-card>
          <p class="text-cmn-sm text-text-secondary leading-relaxed">{{ message }}</p>
        </cmn-card>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class McpConnectComponent {
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected message = 'Preparing Finance Sentry MCP authorization...';

  constructor() {
    effect(() => {
      const redirectUri = this.route.snapshot.queryParamMap.get('redirectUri');
      const state = this.route.snapshot.queryParamMap.get('state');
      if (!redirectUri || !state) {
        this.message = 'Missing MCP authorization parameters.';
        return;
      }

      if (!this.authStore.isAuthenticated()) {
        const returnUrl = this.router
          .createUrlTree([AppRoute.McpConnect], {
            queryParams: {redirectUri, state},
          })
          .toString();
        void this.router.navigate([AppRoute.Login], {
          queryParams: {returnUrl},
        });
        return;
      }

      const authorizeUrl = new URL(`${environment.apiBaseUrl}/auth/mcp/authorize`);
      authorizeUrl.searchParams.set('redirectUri', redirectUri);
      authorizeUrl.searchParams.set('state', state);
      window.location.assign(authorizeUrl.toString());
    });
  }
}
