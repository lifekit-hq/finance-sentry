import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {AsyncStateComponent, ListItemRowComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {BrokeragePnlStore} from '../../store/brokerage-pnl/brokerage-pnl.store';

/**
 * The brokerage book's money-in and what it earned (issue 982): net deposited, realized P&L and unrealized P&L,
 * each a plain row like the performance panel's. Realized and unrealized are put over the net deposited when the
 * figure is complete; a partial or unverifiable one says what it covers or "Basis unavailable", never a zero
 * and never a guessed number.
 */
@Component({
  selector: 'fns-brokerage-pnl',
  imports: [AsyncStateComponent, ListItemRowComponent, MoneyPipe, SkeletonComponent],
  templateUrl: './brokerage-pnl.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BrokeragePnlStore],
})
export class BrokeragePnlComponent {
  public readonly store = inject(BrokeragePnlStore);
}
