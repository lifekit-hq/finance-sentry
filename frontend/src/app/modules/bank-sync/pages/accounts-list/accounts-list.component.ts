import {DecimalPipe, NgTemplateOutlet} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  OnInit,
  ViewContainerRef,
} from '@angular/core';
import {toObservable} from '@angular/core/rxjs-interop';
import {ActivatedRoute, Router, RouterLink} from '@angular/router';
import {formatRelativeTime} from '@lifekit-hq/core/format';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  DisclosureRowComponent,
  EmptyStateComponent,
  IconComponent,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  ListSectionComponent,
  MenuComponent,
  type MenuItem,
  type Nullable,
  SkeletonComponent,
  ToastService,
} from '@lifekit-hq/ui';
import {filter, map, pairwise, take} from 'rxjs';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {DisclosureToggleDirective} from '../../../../shared/directives/disclosure-toggle.directive';
import {ScrollIntoViewDirective} from '../../../../shared/directives/scroll-into-view.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type AccountBalanceItem,
  type AccountCategory,
  type Institution,
} from '../../../../shared/models/wealth/wealth.model';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {SyncStatusLabelPipe} from '../../../../shared/pipes/sync-status-label.pipe';
import {SyncStatusVariantPipe} from '../../../../shared/pipes/sync-status-variant.pipe';
import {UnitPricePipe} from '../../../../shared/pipes/unit-price.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {DialogUtils} from '../../../../shared/utils/dialog.utils';
import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {BrokerageGrowthComponent} from '../../../holdings/components/brokerage-growth/brokerage-growth.component';
import {GrowthOfTenThousandComponent} from '../../../holdings/components/growth-of-ten-thousand/growth-of-ten-thousand.component';
import {type PositionRow} from '../../../holdings/store/holdings.computed';
import {HoldingsStore} from '../../../holdings/store/holdings.store';
import {ConnectModalComponent} from '../../components/connect-modal/connect-modal.component';
import {DisconnectDialogComponent} from '../../components/disconnect-dialog/disconnect-dialog.component';
import {ManualHoldingDialogComponent} from '../../components/manual-holding-dialog/manual-holding-dialog.component';
import {RemoveClosedAccountDialogComponent} from '../../components/remove-closed-account-dialog/remove-closed-account-dialog.component';
import {CLOSED_ACCOUNTS_KEY} from '../../constants/accounts-disclosure/accounts-disclosure.constants';
import {
  MANUAL_HOLDING_MENU,
  MENU_ACTION_EDIT,
  MENU_ACTION_REMOVE_MANUAL,
} from '../../constants/manual-holding/manual-holding.constants';
import {type ClosedAccountRow} from '../../models/account-lifecycle/account-lifecycle.model';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {
  type ManualHolding,
  type ManualHoldingDialogData,
  type ManualHoldingInput,
} from '../../models/manual-holding/manual-holding.model';
import {AccountBalancePipe} from '../../pipes/account-balance.pipe';
import {PositionQuantityPipe} from '../../pipes/position-quantity.pipe';
import {PositionValuePipe} from '../../pipes/position-value.pipe';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsDisclosureUtils} from '../../utils/accounts-disclosure.utils';
import {InstitutionNavUtils} from '../../utils/institution-nav.utils';
import {
  CLOSED_ACCOUNT_MENU,
  CLOSED_REPLACED_ACCOUNT_MENU,
  DISCONNECT_ONLY_MENU,
  MENU_ACTION_DISCONNECT,
  MENU_ACTION_RECONNECT,
  MENU_ACTION_REMOVE,
  MENU_ACTION_REOPEN,
  RECONNECT_MENU,
  RECONNECT_TARGET,
} from './accounts-list.constants';

const SKELETON_ROWS = 3;
const SKELETON_SECTIONS = 2;
/** The holdings feed values every position in USD. */
const FEED_CURRENCY = 'USD';

@Component({
  selector: 'fns-accounts-list',
  imports: [
    AccountBalancePipe,
    AlertComponent,
    AssetLogoPipe,
    AsyncStateComponent,
    BrokerageGrowthComponent,
    ButtonComponent,
    CardComponent,
    DecimalPipe,
    DisclosureRowComponent,
    DisclosureToggleDirective,
    EmptyStateComponent,
    GrowthOfTenThousandComponent,
    HeadlineBadgeComponent,
    IconComponent,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    ListItemRowComponent,
    ListSectionComponent,
    MenuComponent,
    MoneyPipe,
    UnitPricePipe,
    NgTemplateOutlet,
    PositionQuantityPipe,
    PositionValuePipe,
    RouterLink,
    ScrollIntoViewDirective,
    SkeletonComponent,
    SyncStatusLabelPipe,
    SyncStatusVariantPipe,
  ],
  templateUrl: './accounts-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [HoldingsStore],
})
export class AccountsListComponent implements OnInit {
  private readonly injector = inject(Injector);
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectStore = inject(ConnectStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);
  private readonly toast = inject(ToastService);

  /** Providers listed by exactly one institution: only those can take their positions from the holdings feed. */
  private readonly singleInstitutionProviders = computed(() => {
    const counts = new Map<string, number>();
    for (const section of this.store.categorySections()) {
      for (const institution of section.summary.institutions) {
        counts.set(institution.provider, (counts.get(institution.provider) ?? 0) + 1);
      }
    }
    return new Set([...counts].filter(([, count]) => count === 1).map(([provider]) => provider));
  });
  public readonly store = inject(AccountsStore);
  public readonly holdings = inject(HoldingsStore);
  /** The `?provider=` a link named: that institution's accordion opens and scrolls into view. */
  public readonly deepLinkProvider = this.route.snapshot.queryParamMap.get('provider');
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
  public readonly pnlPositiveClass = GAIN_TEXT_CLASS;
  public readonly transactionsRoute = AppRoute.Transactions;
  public readonly assetDossierRoute = AppRoute.AssetDossier;
  public readonly pnlNegativeClass = LOSS_TEXT_CLASS;
  public readonly closedAccountsKey = CLOSED_ACCOUNTS_KEY;
  public readonly manualHoldingMenu = MANUAL_HOLDING_MENU;
  /** Closed unless this device opened it: the section is a record, not something to read on every visit. */
  public readonly closedOpen = computed(
    () => this.store.institutionOpen()[CLOSED_ACCOUNTS_KEY] ?? false
  );

  public ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const connected = params.get('connected');
    const connectError = params.get('connectError');
    const reconnect = params.get('reconnect');

    this.store.openProvider(this.deepLinkProvider);
    // A summary reload (connect, reconnect, a manual-holding edit, Retry) reloads the positions feed too, so an
    // institution's header total and its feed-backed rows stay in step.
    this.holdings.loadPositions(
      toObservable(this.store.lastSyncedAt, {injector: this.injector}).pipe(
        pairwise(),
        filter(([previous]) => previous !== null),
        map(() => undefined)
      )
    );

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

  /**
   * The holdings feed's rows for a broker or exchange, with P&L and today's move; null while the feed has
   * nothing for it (not loaded, failed, shared by several institutions, or a base currency other than USD, which
   * the USD feed cannot add up to), when the summary's rows stand in.
   */
  public holdingRows(institution: Institution): Nullable<PositionRow[]> {
    if (
      this.store.baseCurrency() !== FEED_CURRENCY ||
      !this.singleInstitutionProviders().has(institution.provider)
    ) {
      return null;
    }
    return this.holdings.positionsByProvider()[institution.provider] ?? null;
  }

  public connectAccount(): void {
    this.connectStore.openModal();
    this.dialog.open(ConnectModalComponent, {
      title: 'Connect account',
      size: 'md',
      phone: 'full-screen',
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
      phone: 'full-screen',
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
      phone: 'action-sheet',
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

  public hiddenKey(institution: Institution): string {
    return `${this.disclosureKey(institution)}:hidden`;
  }

  /** The "N hidden" row starts closed and stays as the owner left it on this device. */
  public hiddenOpen(institution: Institution): boolean {
    return this.store.institutionOpen()[this.hiddenKey(institution)] ?? false;
  }

  /** Both figures come from the API: the total is signed like the institution's own and already inside it. */
  public hiddenLabel(institution: Institution): string {
    const total = MoneyUtils.format(institution.hiddenTotal ?? 0, this.store.baseCurrency());
    return `${institution.hiddenCount} hidden · ${total}`;
  }

  /** Whether any of a card's rows belongs to the visible (`hidden` false) or the hidden list. */
  public hasRows(accounts: AccountBalanceItem[], hidden: boolean): boolean {
    return accounts.some(account => !!account.hiddenReason === hidden);
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

  public closedAccountMenu(row: ClosedAccountRow): MenuItem[] {
    return row.reopenable ? CLOSED_ACCOUNT_MENU : CLOSED_REPLACED_ACCOUNT_MENU;
  }

  public onClosedAccountAction(row: ClosedAccountRow, action: string): void {
    if (action === MENU_ACTION_REOPEN) {
      this.store.reopenAccount(row.accountId);
    } else if (action === MENU_ACTION_REMOVE) {
      this.removeClosed(row);
    }
  }

  public removeClosed(row: ClosedAccountRow): void {
    const ref = this.dialog.open<boolean>(RemoveClosedAccountDialogComponent, {
      title: `Remove ${row.bankName} •••• ${row.last4}`,
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
      data: {accountLabel: `${row.bankName} •••• ${row.last4}`},
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.disconnectAccount(row.accountId);
        }
      });
  }

  /** Adds a value by hand, or edits `holding`; the reload brings it into every total. */
  public openManualHolding(holding: Nullable<ManualHolding> = null): void {
    const data: ManualHoldingDialogData = {holding};
    this.dialog
      .open<ManualHoldingInput>(ManualHoldingDialogComponent, {
        title: holding ? `Edit ${holding.label}` : 'Add a value by hand',
        size: 'sm',
        viewContainerRef: this.viewContainerRef,
        data,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(input => {
        if (input) {
          this.store.saveManualHolding({id: holding?.id ?? null, input});
        }
      });
  }

  public onManualHoldingAction(holding: ManualHolding, action: string): void {
    if (action === MENU_ACTION_EDIT) {
      this.openManualHolding(holding);
    } else if (action === MENU_ACTION_REMOVE_MANUAL) {
      this.removeManualHolding(holding);
    }
  }

  public removeManualHolding(holding: ManualHolding): void {
    const title = `Remove ${holding.label}?`;
    this.dialog
      .open<boolean>(ConfirmDialogComponent, {
        data: {
          title,
          message: 'It leaves your total net worth. Your linked accounts are not affected.',
          confirmLabel: 'Remove',
          cancelLabel: 'Cancel',
          confirmVariant: 'destructive',
        },
        title: DialogUtils.phoneTitle(title),
        size: 'sm',
        phone: 'action-sheet',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.removeManualHolding(holding.id);
        }
      });
  }

  public disconnectInstitution(institution: Institution): void {
    const ref = this.dialog.open<boolean>(DisconnectDialogComponent, {
      title: `Disconnect ${institution.name}`,
      size: 'sm',
      phone: 'action-sheet',
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
