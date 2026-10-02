import {ChangeDetectionStrategy, Component, inject, ViewContainerRef} from '@angular/core';
import {RouterLink, RouterLinkActive, RouterOutlet} from '@angular/router';
import {CmnDialogService, PageHeaderComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {ConnectModalComponent} from '../../components/connect-modal/connect-modal.component';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';

interface AccountsTab {
  label: string;
  route: string;
}

// Both stores live on the shell so the Connect action in the header and the routed tab pages
// share one instance: the connect forms reload the accounts store they find above them.
@Component({
  selector: 'fns-accounts-shell',
  imports: [PageHeaderComponent, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AccountsStore, ConnectStore],
  template: `
    <div class="page-container">
      <cmn-page-header
        (actionClick)="connectAccount()"
        title="Accounts"
        subtitle="Balances and holdings across every provider"
        actionLabel="Connect Account"
        actionIcon="Plus"
      />

      <nav class="mt-cmn-6 mb-cmn-6 flex gap-cmn-2 border-b border-border-default" role="tablist">
        @for (tab of tabs; track tab.route) {
          <a
            [routerLink]="tab.route"
            [routerLinkActiveOptions]="{exact: false}"
            routerLinkActive
            ariaCurrentWhenActive="page"
            class="border-b-2 border-transparent px-cmn-4 py-cmn-2 text-cmn-sm font-medium text-text-secondary transition-colors hover:text-text-primary aria-[current=page]:border-accent-default aria-[current=page]:text-text-primary"
            role="tab"
          >
            {{ tab.label }}
          </a>
        }
      </nav>

      <router-outlet />
    </div>
  `,
})
export class AccountsShellComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectStore = inject(ConnectStore);

  public readonly tabs: AccountsTab[] = [
    {label: 'Inventory', route: AppRoute.AccountsList},
    {label: 'Investments', route: AppRoute.AccountsInvestments},
  ];

  public connectAccount(): void {
    this.connectStore.openModal();
    this.dialog.open(ConnectModalComponent, {
      title: 'Connect account',
      size: 'md',
      viewContainerRef: this.viewContainerRef,
    });
  }
}
