import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {SkeletonComponent} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';

/**
 * The brokerage book against SPY on one line (finance-framework P2): the book's time-weighted return (M3)
 * and the benchmark's (M9) from the first day of the longest window both were measured over, so deposits and
 * withdrawals move neither. Renders nothing while no window was measured for both.
 */
@Component({
  selector: 'fns-book-vs-spy',
  imports: [DatePipe, HeadlineBadgeComponent, SkeletonComponent],
  templateUrl: './book-vs-spy.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BookPerformanceStore],
})
export class BookVsSpyComponent {
  public readonly store = inject(BookPerformanceStore);
}
