import {DatePipe, SlicePipe, UpperCasePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject, ViewContainerRef} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Router} from '@angular/router';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDialogService,
  CmnPageActionsService,
  ConfirmDialogComponent,
  EmptyStateComponent,
  IconComponent,
  ListItemRowComponent,
  MenuComponent,
  type MenuItem,
  PageContainerComponent,
  StatCardComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {SUBSCRIPTIONS_ADD_ACTION} from '../../../../shared/constants/page-actions/page-actions.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
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
    ButtonComponent,
    CardComponent,
    AlertComponent,
    ChipComponent,
    DatePipe,
    EmptyStateComponent,
    IconComponent,
    InstallmentProgressPipe,
    ListItemRowComponent,
    MenuComponent,
    MerchantColorPipe,
    MoneyPipe,
    SlicePipe,
    StatCardComponent,
    UpperCasePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [SubscriptionsStore],
  templateUrl: './subscriptions.component.html',
})
export class SubscriptionsComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly router = inject(Router);
  private readonly viewContainerRef = inject(ViewContainerRef);

  public readonly store = inject(SubscriptionsStore);
  public readonly sortOptions = SORT_OPTIONS;
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
      size: 'sm',
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
      size: 'sm',
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
