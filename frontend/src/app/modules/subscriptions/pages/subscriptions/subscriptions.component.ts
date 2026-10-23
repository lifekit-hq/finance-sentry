import {DatePipe, SlicePipe, UpperCasePipe} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
  ViewContainerRef,
} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Router} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  CmnPageActionsService,
  ConfirmDialogComponent,
  EmptyStateComponent,
  IconComponent,
  ListItemRowComponent,
  MenuComponent,
  type MenuItem,
  type Nullable,
  PageContainerComponent,
  PageScrollComponent,
  SkeletonComponent,
  StatCardComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {SUBSCRIPTIONS_ADD_ACTION} from '../../../../shared/constants/page-actions/page-actions.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {DialogUtils} from '../../../../shared/utils/dialog.utils';
import {AddCommitmentDialogComponent} from '../../components/add-commitment-dialog/add-commitment-dialog.component';
import {SetTermDialogComponent} from '../../components/set-term-dialog/set-term-dialog.component';
import {CADENCE_LABELS} from '../../constants/subscription/subscription-form.constants';
import {type CommitmentDialogData} from '../../models/commitment-candidate/commitment-dialog.model';
import {
  type AddCommitmentRequest,
  type LinkCommitmentRequest,
  type Subscription,
  type SubscriptionSort,
} from '../../models/subscription/subscription.model';
import {InstallmentProgressPipe} from '../../pipes/installment-progress.pipe';
import {MerchantColorPipe} from '../../pipes/merchant-color.pipe';
import {SubscriptionsStore} from '../../store/subscriptions/subscriptions.store';
import {SubscriptionUtils} from '../../utils/subscription.utils';
import {NO_ACTIVE_SUBSCRIPTIONS_COPY} from './subscriptions.constants';

const SKELETON_ROWS = 3;

const SORT_OPTIONS: {value: SubscriptionSort; label: string}[] = [
  {value: 'date', label: 'Next charge'},
  {value: 'amount', label: 'Amount'},
  {value: 'name', label: 'Name'},
];

const LINK_MENU_ITEM: MenuItem = {id: 'link', label: 'Link transaction', icon: 'Link'};

type MenuSubscription = Pick<Subscription, 'isTracked' | 'isManual' | 'merchantName'>;

const CHARGES_MENU_ITEM: MenuItem = {id: 'charges', label: 'View charges', icon: 'Receipt'};

const INSTALLMENT_MENU_ITEMS: MenuItem[] = [
  {id: 'term', label: 'Set term', icon: 'Pencil'},
  {id: 'done', label: 'Mark as done', icon: 'Check'},
  {id: 'delete', label: 'Delete', icon: 'Trash2', destructive: true},
];

const UNLINKED_INSTALLMENT_MENU_ITEMS: MenuItem[] = [LINK_MENU_ITEM, ...INSTALLMENT_MENU_ITEMS];

const SUBSCRIPTION_MENU_ITEMS: MenuItem[] = [
  {id: 'dismiss', label: 'Dismiss', icon: 'X', destructive: true},
];

const UNLINKED_SUBSCRIPTION_MENU_ITEMS: MenuItem[] = [LINK_MENU_ITEM, ...SUBSCRIPTION_MENU_ITEMS];

@Component({
  selector: 'fns-subscriptions',
  imports: [
    PageContainerComponent,
    PageScrollComponent,
    ButtonComponent,
    CardComponent,
    AlertComponent,
    AsyncStateComponent,
    DatePipe,
    EmptyStateComponent,
    IconComponent,
    InstallmentProgressPipe,
    ListItemRowComponent,
    MenuComponent,
    MerchantColorPipe,
    MoneyPipe,
    SkeletonComponent,
    SlicePipe,
    StatCardComponent,
    UpperCasePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [SubscriptionsStore],
  templateUrl: './subscriptions.component.html',
})
export class SubscriptionsComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly router = inject(Router);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(SubscriptionsStore);
  public readonly skeletonRowCount = SKELETON_ROWS;
  /** Nothing tracked at all: with too little history to detect any, the page explains that instead. */
  public readonly isEmpty = computed(
    () => this.store.subscriptions().length === 0 && !this.store.hasInsufficientHistory()
  );
  // Offline only reaches `resolve` while nothing is on screen: with rows, a failed reload keeps
  // its error banner instead of reading as a successful (stale) load.
  public readonly view = computed(() => {
    const hasData = this.store.subscriptions().length > 0;
    return AsyncViewUtils.resolve({
      isLoading: this.store.isLoadLoading(),
      hasData,
      errorMessage: this.store.loadErrorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly noActiveCopy = NO_ACTIVE_SUBSCRIPTIONS_COPY;
  public readonly sortLabel = computed(
    () => SORT_OPTIONS.find(o => o.value === this.store.sort())?.label ?? ''
  );
  public readonly sortItems = computed<MenuItem[]>(() =>
    SORT_OPTIONS.map(o => ({
      id: o.value,
      label: o.label,
      icon: o.value === this.store.sort() ? 'Check' : undefined,
    }))
  );
  public readonly cadenceLabels = CADENCE_LABELS;

  constructor() {
    inject(CmnPageActionsService)
      .on(SUBSCRIPTIONS_ADD_ACTION)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.openAdd());
  }

  public subscriptionMenuItems(sub: MenuSubscription): MenuItem[] {
    const items = sub.isTracked ? SUBSCRIPTION_MENU_ITEMS : UNLINKED_SUBSCRIPTION_MENU_ITEMS;
    return SubscriptionUtils.chargesQuery(sub) ? [CHARGES_MENU_ITEM, ...items] : items;
  }

  public installmentMenuItems(item: MenuSubscription): MenuItem[] {
    const items = item.isTracked ? INSTALLMENT_MENU_ITEMS : UNLINKED_INSTALLMENT_MENU_ITEMS;
    return SubscriptionUtils.chargesQuery(item) ? [CHARGES_MENU_ITEM, ...items] : items;
  }

  /** Opens the ledger narrowed to the row's charges (its search box reads `q`). */
  public viewCharges(item: MenuSubscription): void {
    const q = SubscriptionUtils.chargesQuery(item);
    if (q) {
      void this.router.navigate([AppRoute.Transactions], {queryParams: {q}});
    }
  }

  public onSortSelect(id: string): void {
    this.setSort(id as SubscriptionSort);
  }

  public setSort(sort: SubscriptionSort): void {
    this.store.setSort(sort);
  }

  public openAdd(): void {
    this.dialog
      .open<AddCommitmentRequest>(AddCommitmentDialogComponent, {
        title: 'Add subscription or installment',
        size: 'md',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(payload => {
        if (payload) {
          this.store.addCommitment(payload);
        }
      });
  }

  public openLink(item: Pick<Subscription, 'id' | 'merchantName' | 'kind'>): void {
    this.dialog
      .open<LinkCommitmentRequest>(AddCommitmentDialogComponent, {
        title: `Link ${item.merchantName} to a transaction`,
        data: {linkTo: item.merchantName, kind: item.kind} satisfies CommitmentDialogData,
        size: 'md',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(request => {
        if (request) {
          this.store.linkCommitment({id: item.id, request});
        }
      });
  }

  public onInstallmentAction(action: string, item: Subscription): void {
    if (action === 'charges') {
      this.viewCharges(item);
    } else if (action === 'link') {
      this.openLink(item);
    } else if (action === 'term') {
      this.openSetTerm(item);
    } else if (action === 'done') {
      this.store.completeInstallment(item.id);
    } else if (action === 'delete') {
      this.deleteInstallment(item);
    }
  }

  public onSubscriptionAction(action: string, sub: Subscription): void {
    if (action === 'charges') {
      this.viewCharges(sub);
    } else if (action === 'link') {
      this.openLink(sub);
    } else if (action === 'dismiss') {
      this.dismiss(sub);
    }
  }

  public dismiss(sub: Pick<Subscription, 'id' | 'merchantName'>): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: `Dismiss ${sub.merchantName}?`,
        message: `This will hide ${sub.merchantName} from your subscriptions. It will survive future detection runs.`,
        confirmLabel: 'Dismiss',
        cancelLabel: 'Keep',
        confirmVariant: 'destructive',
      },
      title: DialogUtils.phoneTitle(`Dismiss ${sub.merchantName}?`),
      size: 'sm',
      phone: 'action-sheet',
      cancelLabel: 'Keep',
      viewContainerRef: this.viewContainerRef,
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.dismiss(sub.id);
        }
      });
  }

  public restore(id: string): void {
    this.store.restore(id);
  }

  public deleteInstallment(sub: Pick<Subscription, 'id' | 'merchantName'>): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: `Delete ${sub.merchantName}?`,
        message: 'Permanently remove this installment.',
        confirmLabel: 'Delete',
        cancelLabel: 'Keep',
        confirmVariant: 'destructive',
      },
      title: DialogUtils.phoneTitle(`Delete ${sub.merchantName}?`),
      size: 'sm',
      phone: 'action-sheet',
      cancelLabel: 'Keep',
      viewContainerRef: this.viewContainerRef,
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.deleteInstallment(sub.id);
        }
      });
  }

  private openSetTerm(item: Pick<Subscription, 'id' | 'merchantName' | 'termCount'>): void {
    this.dialog
      .open<Nullable<number>>(SetTermDialogComponent, {
        title: `${item.merchantName} — term`,
        data: {termCount: item.termCount},
        size: 'sm',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(termCount => {
        if (termCount !== undefined) {
          this.store.setInstallmentTerm({id: item.id, termCount});
        }
      });
  }
}
