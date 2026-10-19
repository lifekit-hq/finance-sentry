import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {AsyncStateComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  GROWTH_OF_TEN_THOUSAND_AMOUNT,
  GROWTH_OF_TEN_THOUSAND_CURRENCY,
} from '../../constants/book-performance/book-performance.constants';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';

/**
 * Growth of 10,000 (finance-framework P2): what one sum put into the brokerage book on the first day of the
 * longest measured window would be worth now, against the same sum in SPY - the book's time-weighted return
 * (M3) against the benchmark's (M9), so deposits and withdrawals do not move either line.
 */
@Component({
  selector: 'fns-growth-of-ten-thousand',
  imports: [AsyncStateComponent, DatePipe, HeadlineBadgeComponent, MoneyPipe, SkeletonComponent],
  templateUrl: './growth-of-ten-thousand.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BookPerformanceStore],
})
export class GrowthOfTenThousandComponent {
  public readonly store = inject(BookPerformanceStore);
  public readonly amount = GROWTH_OF_TEN_THOUSAND_AMOUNT;
  public readonly currency = GROWTH_OF_TEN_THOUSAND_CURRENCY;
}
