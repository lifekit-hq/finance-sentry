import {DatePipe, NgTemplateOutlet} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject, input} from '@angular/core';
import {
  AsyncStateComponent,
  CardComponent,
  IconComponent,
  ListItemRowComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {BOOK_VS_SPY_BASIS_NOTE} from '../../constants/book-performance/book-performance.constants';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';

/**
 * The brokerage book against SPY (and QQQ over the same dates, when measured) as one row (finance-framework P2): the book's time-weighted return (M3)
 * and the benchmark's (M9) from the first day of the longest window both were measured over, so deposits and
 * withdrawals move neither. Renders nothing while no window was measured for both, and a retryable error when
 * the request fails. Beside the since-start window it shows the 3M window, the sleeve's stored
 * sustained-underperformance flag when set, and one basis line (money-semantics §10) under the figures.
 */
@Component({
  selector: 'fns-book-vs-spy',
  imports: [
    AsyncStateComponent,
    CardComponent,
    IconComponent,
    DatePipe,
    HeadlineBadgeComponent,
    ListItemRowComponent,
    NgTemplateOutlet,
    SkeletonComponent,
  ],
  templateUrl: './book-vs-spy.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BookPerformanceStore],
})
export class BookVsSpyComponent {
  public readonly store = inject(BookPerformanceStore);
  public readonly basisNote = BOOK_VS_SPY_BASIS_NOTE;
  /** Home draws it as its own titled card; Accounts embeds the rows in the brokerage panel. */
  public readonly card = input(false);
}
