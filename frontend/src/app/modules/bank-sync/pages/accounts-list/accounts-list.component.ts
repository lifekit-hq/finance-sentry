import {BreakpointObserver} from '@angular/cdk/layout';
import {DecimalPipe, NgTemplateOutlet} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject, OnInit, ViewContainerRef} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {ActivatedRoute, Router} from '@angular/router';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  DisclosureRowComponent,
  EmptyStateComponent,
  IconComponent,
  InstitutionAvatarComponent,
  MenuComponent,
  type MenuItem,
  SkeletonComponent,
  ToastService,
} from '@lifekit-hq/ui';
import {map, take} from 'rxjs';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type Institution} from '../../../../shared/models/wealth/wealth.model';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {NetBalancePipe} from '../../../../shared/pipes/net-balance.pipe';
import {RelativeTimePipe} from '../../../../shared/pipes/relative-time.pipe';
import {SyncStatusLabelPipe} from '../../../../shared/pipes/sync-status-label.pipe';
import {SyncStatusVariantPipe} from '../../../../shared/pipes/sync-status-variant.pipe';
import {TimeUtils} from '../../../../shared/utils/time.utils';
import {ConnectModalComponent} from '../../components/connect-modal/connect-modal.component';
import {DisconnectDialogComponent} from '../../components/disconnect-dialog/disconnect-dialog.component';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {AccountBalancePipe} from '../../pipes/account-balance.pipe';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {
  DISCONNECT_ONLY_MENU,
  MENU_ACTION_DISCONNECT,
  MENU_ACTION_RECONNECT,
  PHONE_MEDIA_QUERY,
  RECONNECT_MENU,
  RECONNECTABLE_PROVIDER,
  SYNC_DOT_CLASS,
} from './accounts-list.constants';

const SKELETON_ROWS = 5;

@Component({
  selector: 'fns-accounts-list',
  imports: [
    AccountBalancePipe,
    AlertComponent,
    AssetLogoPipe,
    ButtonComponent,
    CardComponent,
    DecimalPipe,
    DisclosureRowComponent,
    EmptyStateComponent,
    IconComponent,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    MenuComponent,
    MoneyPipe,
    NetBalancePipe,
    NgTemplateOutlet,
    RelativeTimePipe,
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
  private readonly toast = inject(ToastService);
  private readonly isPhone = toSignal(
    inject(BreakpointObserver)
      .observe(PHONE_MEDIA_QUERY)
      .pipe(map(state => state.matches)),
    {initialValue: false}
  );

  public readonly store = inject(AccountsStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
  public readonly dotClass = SYNC_DOT_CLASS;

  public ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const connected = params.get('connected');
    const connectError = params.get('connectError');

    if (connected === 'truelayer') {
      this.toast.show('Bank connected. Syncing your accounts now.', 'success');
      this.clearConnectQueryParams();
      this.store.load();
    } else if (connectError) {
      this.toast.show(`Connection failed: ${connectError}`, 'error');
      this.clearConnectQueryParams();
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

  public reconnect(): void {
    this.connectStore.openModal();
    this.connectStore.selectInstitutionType('bank');
    this.connectStore.selectPickedProvider('truelayer');
    this.dialog.open(ConnectModalComponent, {
      title: 'Reconnect bank',
      size: 'md',
      viewContainerRef: this.viewContainerRef,
    });
  }

  private clearConnectQueryParams(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {connected: null, connectError: null},
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

  public rowCountLabel(institution: Institution, noun: string): string {
    const count = institution.cards?.length || institution.accounts.length;
    const label = institution.cards?.length ? 'card' : noun;
    return `${count} ${label}${count === 1 ? '' : 's'}`;
  }

  /** Row subtitle: on a phone the sync status moves here as text (not colour alone). */
  public rowSublabel(institution: Institution, noun: string): string {
    const count = this.rowCountLabel(institution, noun);
    return this.isPhone() ? `${count} · ${this.syncSummary(institution)}` : count;
  }

  public syncSummary(institution: Institution): string {
    if (institution.syncStatus === 'reauth_required') {
      return 'Reconnect needed';
    }
    if (institution.syncStatus === 'failed') {
      return 'Sync failed';
    }
    return `synced ${TimeUtils.getRelativeTime(institution.lastSyncTimestamp).toLowerCase()}`;
  }

  public needsAttention(institution: Institution): boolean {
    return institution.syncStatus === 'failed' || institution.syncStatus === 'reauth_required';
  }

  public canReconnect(institution: Institution): boolean {
    return institution.provider === RECONNECTABLE_PROVIDER;
  }

  public institutionMenu(institution: Institution): MenuItem[] {
    return this.canReconnect(institution) ? RECONNECT_MENU : DISCONNECT_ONLY_MENU;
  }

  public onInstitutionAction(institution: Institution, action: string): void {
    if (action === MENU_ACTION_RECONNECT) {
      this.reconnect();
    } else if (action === MENU_ACTION_DISCONNECT) {
      this.disconnectInstitution(institution);
    }
  }

  public navigateToDossier(symbol: string): void {
    void this.router.navigate([AppRoute.AssetDossier, symbol]);
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
