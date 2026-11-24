import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {AsyncStateComponent, ListItemRowComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {BookIncomeStore} from '../../store/book-income/book-income.store';

/**
 * Income received by the brokerage book (dividends, interest, staking): trailing 12 months and year to date with the
 * tax withheld shown beside the net, the forward income estimate and the yield on cost, then one row per holding.
 * A view over rows already collected: each figure stands alone, and an estimate or yield with no input reads
 * "Not available", never zero. Renders nothing for a book that has received no income and holds no payer.
 */
@Component({
  selector: 'fns-book-income',
  imports: [AsyncStateComponent, DatePipe, ListItemRowComponent, MoneyPipe, SkeletonComponent],
  templateUrl: './book-income.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BookIncomeStore],
})
export class BookIncomeComponent {
  public readonly store = inject(BookIncomeStore);
}
