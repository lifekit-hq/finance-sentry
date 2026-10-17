import {DecimalPipe, NgTemplateOutlet} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  ViewContainerRef,
} from '@angular/core';
import {ActivatedRoute, Router, RouterLink} from '@angular/router';
import {formatRelativeTime} from '@lifekit-hq/core/format';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  DisclosureRowComponent,
  EmptyStateComponent,
  IconComponent,
  InstitutionAvatarComponent,
  ListSectionComponent,
  MenuComponent,
  type MenuItem,
  SkeletonComponent,
  ToastService,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {DisclosureToggleDirective} from '../../../../shared/directives/disclosure-toggle.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type AccountCategory,
  type Institution,
} from '../../../../shared/models/wealth/wealth.model';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {SyncStatusLabelPipe} from '../../../../shared/pipes/sync-status-label.pipe';
import {SyncStatusVariantPipe} from '../../../../shared/pipes/sync-status-variant.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {ConnectModalComponent} from '../../components/connect-modal/connect-modal.component';
import {DisconnectDialogComponent} from '../../components/disconnect-dialog/disconnect-dialog.component';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {AccountBalancePipe} from '../../pipes/account-balance.pipe';
import {PositionQuantityPipe} from '../../pipes/position-quantity.pipe';
import {PositionValuePipe} from '../../pipes/position-value.pipe';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsDisclosureUtils} from '../../utils/accounts-disclosure.utils';
import {InstitutionNavUtils} from '../../utils/institution-nav.utils';
import {
  DISCONNECT_ONLY_MENU,
  MENU_ACTION_DISCONNECT,
  MENU_ACTION_RECONNECT,
  RECONNECT_MENU,
  RECONNECT_TARGET,
} from './accounts-list.constants';

const SKELETON_ROWS = 3;
const SKELETON_SECTIONS = 2;

@Component({
  selector: 'fns-accounts-list',
  imports: [
    AccountBalancePipe,
    AlertComponent,
    AssetLogoPipe,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    DecimalPipe,
    DisclosureRowComponent,
    DisclosureToggleDirective,
    EmptyStateComponent,
    HeadlineBadgeComponent,
    IconComponent,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    ListSectionComponent,
    MenuComponent,
    MoneyPipe,
    NgTemplateOutlet,
    PositionQuantityPipe,
    PositionValuePipe,
    RouterLink,
    SkeletonComponent,
    SyncStatusLabelPipe,
    SyncStatusVariantPipe,
  ],
  templateUrl: './accounts-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountsListComponent implements OnInit {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectStore = inject(ConnectStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);
  private readonly toast = inject(ToastService);

  public readonly store = inject(AccountsStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
  public readonly skeletonSections = Array.from({length: SKELETON_SECTIONS});
  public readonly attentionInstitutions = computed(() =>
    this.store
      .categorySections()
      .flatMap(section => section.summary.institutions)
      .filter(institution => this.needsAttention(institution))
  );
  public readonly view = computed(() =>
    AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData: this.store.summary() !== null,
      errorMessage: this.store.errorMessage(),
      offline: this.connectivity.offline(),
      lastSyncedAt: this.store.lastSyncedAt(),
    })
  );
  public readonly investmentsRoute = AppRoute.AccountsInvestments;

  public ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const connected = params.get('connected');
    const connectError = params.get('connectError');
    const reconnect = params.get('reconnect');

    if (connected === 'truelayer') {
      this.toast.show('Bank connected. Syncing your accounts now.', 'success');
      this.clearConnectQueryParams();
      this.store.load();
    } else if (connectError) {
      this.toast.show(`Connection failed: ${connectError}`, 'error');
      this.clearConnectQueryParams();
    } else if (reconnect) {
      // A reconnect link (a confirmed credential loss's alert) opens the provider's own reconnect flow where it has
      // one; any other provider lands here, where its row says "Reconnect needed".
      this.clearConnectQueryParams();
      this.reconnect({provider: reconnect});
    }
  }

  public connectAccount(): void {
    this.connectStore.openModal();
    this.dialog.open(ConnectModalComponent, {
      title: 'Connect account',
      size: 'md',
      viewContainerRef: this.viewContainerRef,
    });
  }

  public reconnect(institution: Pick<Institution, 'provider'>): void {
    const target = RECONNECT_TARGET[institution.provider];
    if (!target) {
      return;
    }
    this.connectStore.openModal();
    this.connectStore.selectPickedProvider(target.slug);
    this.dialog.open(ConnectModalComponent, {
      title: target.title,
      size: 'md',
      viewContainerRef: this.viewContainerRef,
    });
  }

  private clearConnectQueryParams(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {connected: null, connectError: null, reconnect: null},
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  public disconnect(account: Pick<BankAccount, 'accountId' | 'bankName' | 'provider'>): void {
    const ref = this.dialog.open<boolean>(DisconnectDialogComponent, {
      title: `Disconnect ${account.bankName}`,
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
      data: {providerName: account.bankName},
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed !== true) {
          return;
        }
        if (account.provider === 'monobank') {
          this.store.disconnectMonobank();
          return;
        }
        this.store.disconnectAccount(account.accountId);
      });
  }

  /**
   * The row's second line, the same for every category: what it holds ("3 accounts", "14 positions ·
   * $3,008 cash"), then the sync state in words only when something is wrong. A healthy row is just the dot.
   */
  public sublabel(institution: Institution, noun: string): string {
    const base = InstitutionNavUtils.sublabel(institution, noun, this.store.baseCurrency());
    const note = this.syncNote(institution);
    return note ? `${base} · ${note}` : base;
  }

  /** What is wrong with the connection, in words; null while it is healthy or merely syncing. */
  public syncNote(institution: Institution): Nullable<string> {
    if (this.needsReconnect(institution)) {
      return 'Reconnect needed';
    }
    if (institution.syncStatus === 'failed') {
      return 'Sync failed';
    }
    if (institution.syncStatus === 'dormant') {
      return 'Dormant';
    }
    if (institution.syncStatus === 'syncing') {
      return null;
    }
    if (!institution.lastSyncTimestamp) {
      return 'Not synced yet';
    }
    const since = formatRelativeTime(institution.lastSyncTimestamp);
    if (institution.syncStatus === 'retrying') {
      return `Retrying ${since}`;
    }
    return institution.syncStatus === 'stale' ? `Stale ${since}` : null;
  }

  /** The avatar dot's accessible name: the sync note, or when it last synced for a healthy row. */
  public statusLabel(institution: Institution): string {
    if (institution.syncStatus === 'syncing') {
      return 'Syncing…';
    }
    const since = institution.lastSyncTimestamp
      ? formatRelativeTime(institution.lastSyncTimestamp)
      : null;
    return this.syncNote(institution) ?? `Last synced ${since ?? 'never'}`;
  }

  /** Open as the owner left it on this device, else the category's default (banks open, brokers closed). */
  public isOpen(institution: Institution, category: AccountCategory): boolean {
    return (
      this.store.institutionOpen()[this.disclosureKey(institution)] ??
      AccountsDisclosureUtils.defaultOpen(category)
    );
  }

  public disclosureKey(institution: Institution): string {
    return AccountsDisclosureUtils.key(institution);
  }

  public needsAttention(institution: Institution): boolean {
    return institution.syncStatus === 'failed' || this.needsReconnect(institution);
  }

  /** Only the owner can fix it: the provider refused the credential (`reconnect_needed` comes from its health). */
  public needsReconnect(institution: Pick<Institution, 'syncStatus'>): boolean {
    return (
      institution.syncStatus === 'reauth_required' || institution.syncStatus === 'reconnect_needed'
    );
  }

  public attentionTitle(institution: Institution): string {
    return this.needsReconnect(institution)
      ? `${institution.name} needs reconnecting`
      : `${institution.name} sync failed`;
  }

  public attentionDetail(institution: Institution): string {
    const last = institution.lastSuccessfulSyncTimestamp;
    if (!last) {
      return 'No successful sync yet';
    }
    const since = formatRelativeTime(last);
    return this.needsReconnect(institution)
      ? `Balances frozen since ${since}`
      : `Last synced ${since}`;
  }

  public canReconnect(institution: Institution): boolean {
    return institution.provider in RECONNECT_TARGET;
  }

  public institutionMenu(institution: Institution): MenuItem[] {
    return this.canReconnect(institution) ? RECONNECT_MENU : DISCONNECT_ONLY_MENU;
  }

  public onInstitutionAction(institution: Institution, action: string): void {
    if (action === MENU_ACTION_RECONNECT) {
      this.reconnect(institution);
    } else if (action === MENU_ACTION_DISCONNECT) {
      this.disconnectInstitution(institution);
    }
  }

  public navigateToDossier(symbol: string): void {
    void this.router.navigate([AppRoute.AssetDossier, symbol]);
  }

  public navigateToTransactions(accountId: string): void {
    void this.router.navigate([AppRoute.Transactions], {queryParams: {account: accountId}});
  }

  public disconnectInstitution(institution: Institution): void {
    const ref = this.dialog.open<boolean>(DisconnectDialogComponent, {
      title: `Disconnect ${institution.name}`,
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
      data: {providerName: institution.name},
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed !== true) {
          return;
        }
        if (institution.provider === 'ibkr') {
          this.store.disconnectIBKR();
          return;
        }
        if (institution.provider === 'inzhur') {
          this.store.disconnectInzhur();
          return;
        }
        if (institution.provider === 'binance') {
          this.store.disconnectBinance();
          return;
        }
        if (institution.provider === 'revolut_x') {
          this.store.disconnectRevolutX();
          return;
        }
        this.store.disconnectInstitution({
          provider: institution.provider,
          institutionId: institution.institutionId,
        });
      });
  }
}
