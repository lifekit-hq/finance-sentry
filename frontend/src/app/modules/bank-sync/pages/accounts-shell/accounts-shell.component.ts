import {ChangeDetectionStrategy, Component, inject, ViewContainerRef} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {RouterOutlet} from '@angular/router';
import {
  CmnDialogService,
  CmnPageActionsService,
  CmnTab,
  PageContainerComponent,
  TabGroupComponent,
} from '@lifekit-hq/ui';

import {ACCOUNTS_CONNECT_ACTION} from '../../../../shared/constants/page-actions/page-actions.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {ConnectModalComponent} from '../../components/connect-modal/connect-modal.component';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';

// Both stores live on the shell so the Connect action in the top bar and the routed tab pages
// share one instance: the connect forms reload the accounts store they find above them.
@Component({
  selector: 'fns-accounts-shell',
  imports: [PageContainerComponent, RouterOutlet, TabGroupComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AccountsStore, ConnectStore],
  template: `
    <cmn-page-container spacing="none">
      <p class="text-cmn-sm text-text-secondary">Balances and holdings across every provider</p>

      <cmn-tab-group [tabs]="tabs" ariaLabel="Accounts view" class="mt-cmn-6 mb-cmn-6" />

      <router-outlet />
    </cmn-page-container>
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

  constructor() {
    inject(CmnPageActionsService)
      .on(ACCOUNTS_CONNECT_ACTION)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.connectAccount());
  }

  public connectAccount(): void {
    this.connectStore.openModal();
    this.dialog.open(ConnectModalComponent, {
      title: 'Connect account',
      size: 'md',
      viewContainerRef: this.viewContainerRef,
    });
  }
}
