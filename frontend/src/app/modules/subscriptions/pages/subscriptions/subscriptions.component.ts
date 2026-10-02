import {DatePipe, SlicePipe, UpperCasePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject, ViewContainerRef} from '@angular/core';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  EmptyStateComponent,
  IconComponent,
  ListItemRowComponent,
  MenuComponent,
  type MenuItem,
  PageHeaderComponent,
  StatCardComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {AddInstallmentDialogComponent} from '../../components/add-installment-dialog/add-installment-dialog.component';
import {AddSubscriptionDialogComponent} from '../../components/add-subscription-dialog/add-subscription-dialog.component';
import {SetTermDialogComponent} from '../../components/set-term-dialog/set-term-dialog.component';
import {CADENCE_LABELS} from '../../constants/subscription/subscription-form.constants';
import {
  type AddInstallmentRequest,
  type AddSubscriptionRequest,
  type Subscription,
  type SubscriptionSort,
} from '../../models/subscription/subscription.model';
import {InstallmentProgressPipe} from '../../pipes/installment-progress.pipe';
import {MerchantColorPipe} from '../../pipes/merchant-color.pipe';
import {SubscriptionsStore} from '../../store/subscriptions/subscriptions.store';

const SORT_OPTIONS: {value: SubscriptionSort; label: string}[] = [
  {value: 'date', label: 'Next charge'},
  {value: 'amount', label: 'Amount'},
  {value: 'name', label: 'Name'},
];

const INSTALLMENT_MENU_ITEMS: MenuItem[] = [
  {id: 'term', label: 'Set term', icon: 'Pencil'},
  {id: 'done', label: 'Mark as done', icon: 'Check'},
  {id: 'delete', label: 'Delete', icon: 'Trash2', destructive: true},
];

const SUBSCRIPTION_MENU_ITEMS: MenuItem[] = [
  {id: 'dismiss', label: 'Dismiss', icon: 'X', destructive: true},
];

@Component({
  selector: 'fns-subscriptions',
  imports: [
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
    PageHeaderComponent,
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
  private readonly viewContainerRef = inject(ViewContainerRef);

  public readonly store = inject(SubscriptionsStore);
  public readonly sortOptions = SORT_OPTIONS;
  public readonly cadenceLabels = CADENCE_LABELS;
  public readonly subscriptionMenuItems = SUBSCRIPTION_MENU_ITEMS;
  public readonly installmentMenuItems = INSTALLMENT_MENU_ITEMS;

  public setSort(sort: SubscriptionSort): void {
    this.store.setSort(sort);
  }

  public openAddSubscription(): void {
    this.dialog
      .open<AddSubscriptionRequest>(AddSubscriptionDialogComponent, {
        title: 'Add subscription',
        size: 'md',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(payload => {
        if (payload) {
          this.store.addSubscription(payload);
        }
      });
  }

  public openAddInstallment(): void {
    this.dialog
      .open<AddInstallmentRequest>(AddInstallmentDialogComponent, {
        title: 'Add installment',
        size: 'md',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(payload => {
        if (payload) {
          this.store.addInstallment(payload);
        }
      });
  }

  public onInstallmentAction(action: string, item: Subscription): void {
    if (action === 'term') {
      this.openSetTerm(item);
    } else if (action === 'done') {
      this.store.completeInstallment(item.id);
    } else if (action === 'delete') {
      this.deleteInstallment(item);
    }
  }

  public onSubscriptionAction(action: string, sub: Subscription): void {
    if (action === 'dismiss') {
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
