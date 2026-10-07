import {ChangeDetectionStrategy, Component, inject, ViewContainerRef} from '@angular/core';
import {RouterOutlet} from '@angular/router';
import {CmnDialogService, CmnTab, PageHeaderComponent, TabGroupComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {ConnectModalComponent} from '../../components/connect-modal/connect-modal.component';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';

// Both stores live on the shell so the Connect action in the header and the routed tab pages
// share one instance: the connect forms reload the accounts store they find above them.
@Component({
  selector: 'fns-accounts-shell',
  imports: [PageHeaderComponent, RouterOutlet, TabGroupComponent],
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

      <cmn-tab-group [tabs]="tabs" ariaLabel="Accounts view" class="mt-cmn-6 mb-cmn-6" />

      <router-outlet />
    </div>
  `,
})
export class AccountsShellComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectStore = inject(ConnectStore);

  public readonly tabs: CmnTab[] = [
    {id: 'inventory', label: 'Inventory', link: AppRoute.AccountsList},
    {id: 'investments', label: 'Investments', link: AppRoute.AccountsInvestments},
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
