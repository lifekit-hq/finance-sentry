import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  ViewContainerRef,
} from '@angular/core';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  IconComponent,
  ListItemRowComponent,
  MenuComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {DialogUtils} from '../../../../shared/utils/dialog.utils';
import {
  INCOME_ACTIVE_MENU,
  INCOME_MENU_ACTION_DELETE,
  INCOME_MENU_ACTION_EDIT,
  INCOME_MENU_ACTION_RETIRE,
  INCOME_RETIRED_MENU,
  INCOME_SKELETON_ROWS,
} from '../../constants/income-source/income-source.constants';
import {
  type IncomeSourceDialogData,
  type IncomeSourceInput,
  type IncomeSourceRow,
} from '../../models/income-source/income-source.model';
import {IncomeSourcesStore} from '../../store/income-sources/income-sources.store';
import {IncomeSourceDialogComponent} from '../income-source-dialog/income-source-dialog.component';

/** The "Money profile" settings section: the income the person declares, so planning has expected income and not only what landed. */
@Component({
  selector: 'fns-money-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    IconComponent,
    ListItemRowComponent,
    MenuComponent,
    MoneyPipe,
    SkeletonComponent,
  ],
  providers: [IncomeSourcesStore],
  templateUrl: './money-profile.component.html',
})
export class MoneyProfileComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(IncomeSourcesStore);
  // Offline only reaches `resolve` while nothing is on screen: with rows, a failed reload keeps its error banner.
  public readonly view = computed(() => {
    const hasData = this.store.rows().length > 0;
    return AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData,
      errorMessage: this.store.sourcesErrorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly skeletonRowCount = INCOME_SKELETON_ROWS;
  public readonly activeMenu = INCOME_ACTIVE_MENU;
  public readonly retiredMenu = INCOME_RETIRED_MENU;

  /** Adds an income source, or edits `source`; the reload brings the saved list back. */
  public openSource(source: IncomeSourceRow | null = null): void {
    const data: IncomeSourceDialogData = {source};
    this.dialog
      .open<IncomeSourceInput>(IncomeSourceDialogComponent, {
        title: source ? `Edit ${source.label}` : 'Add an income source',
        size: 'sm',
        viewContainerRef: this.viewContainerRef,
        data,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(input => {
        if (input) {
          this.store.save({id: source?.id ?? null, input});
        }
      });
  }

  public onAction(source: IncomeSourceRow, action: string): void {
    if (action === INCOME_MENU_ACTION_EDIT) {
      this.openSource(source);
    } else if (action === INCOME_MENU_ACTION_RETIRE) {
      this.confirm(
        `Retire ${source.label}?`,
        'It stops counting as current income and keeps its history. You can still edit or delete it.',
        'Retire',
        () => this.store.retire(source.id)
      );
    } else if (action === INCOME_MENU_ACTION_DELETE) {
      this.confirm(
        `Delete ${source.label}?`,
        'It is removed for good, including its history.',
        'Delete',
        () => this.store.remove(source.id)
      );
    }
  }

  private confirm(
    title: string,
    message: string,
    confirmLabel: string,
    onConfirm: () => void
  ): void {
    this.dialog
      .open<boolean>(ConfirmDialogComponent, {
        data: {title, message, confirmLabel, cancelLabel: 'Cancel', confirmVariant: 'destructive'},
        title: DialogUtils.phoneTitle(title),
        size: 'sm',
        phone: 'action-sheet',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          onConfirm();
        }
      });
  }
}
