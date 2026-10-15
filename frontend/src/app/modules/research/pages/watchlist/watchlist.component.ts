import {DatePipe} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  ViewContainerRef,
} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {RouterLink} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  FormFieldComponent,
  InputComponent,
  PageContainerComponent,
  SkeletonComponent,
  TextareaComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {
  PENDING_ID_PREFIX,
  WATCHLIST_EMPTY_COPY,
} from '../../constants/watchlist/watchlist.constants';
import {type WatchlistRow} from '../../models/watchlist/watchlist.model';
import {WatchlistStore} from '../../store/watchlist/watchlist.store';

const SKELETON_ROWS = 3;
const NOTE_ROWS = 3;
const MAX_TICKER_LENGTH = 20;
const MAX_EXCHANGE_LENGTH = 20;
const MAX_NOTE_LENGTH = 500;

@Component({
  selector: 'fns-watchlist',
  imports: [
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    DatePipe,
    FormFieldComponent,
    InputComponent,
    PageContainerComponent,
    ReactiveFormsModule,
    RouterLink,
    SkeletonComponent,
    TextareaComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [WatchlistStore],
  templateUrl: './watchlist.component.html',
})
export class WatchlistComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(WatchlistStore);
  public readonly emptyCopy = WATCHLIST_EMPTY_COPY;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS}, (_, i) => i);
  public readonly noteRows = NOTE_ROWS;
  public readonly maxTickerLength = MAX_TICKER_LENGTH;
  public readonly maxExchangeLength = MAX_EXCHANGE_LENGTH;
  public readonly maxNoteLength = MAX_NOTE_LENGTH;

  public readonly view = computed(() => {
    const hasData = this.store.items().length > 0;
    return AsyncViewUtils.resolve({
      isLoading: this.store.isLoadLoading(),
      hasData,
      errorMessage: this.store.loadErrorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly isEmpty = computed(() => this.store.items().length === 0);

  public readonly form = new FormGroup({
    ticker: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    exchange: new FormControl('', {nonNullable: true}),
    note: new FormControl('', {nonNullable: true}),
  });

  public isPending(row: WatchlistRow): boolean {
    return row.id.startsWith(PENDING_ID_PREFIX);
  }

  public submit(): void {
    const {ticker, exchange, note} = this.form.getRawValue();
    if (ticker.trim() === '') {
      this.form.markAllAsTouched();
      return;
    }
    if (this.store.isAddLoading() || this.store.isLoadLoading()) {
      return;
    }
    this.store.add({
      payload: {
        ticker: ticker.trim().toUpperCase(),
        exchange: exchange.trim() || null,
        note: note.trim() || null,
      },
      onAdded: () => this.form.reset(),
    });
  }

  public confirmRemove(row: Pick<WatchlistRow, 'id' | 'ticker'>): void {
    this.dialog
      .open<boolean>(ConfirmDialogComponent, {
        data: {
          title: `Remove ${row.ticker}?`,
          message: `${row.ticker} and its note leave your watchlist.`,
          confirmLabel: 'Remove',
          cancelLabel: 'Keep',
          confirmVariant: 'destructive',
        },
        size: 'sm',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.remove(row.id);
        }
      });
  }
}
