import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AlertComponent,
  AsyncStateComponent,
  CardComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  NET_WORTH_BRIDGE_AMOUNT_CLASS,
  NET_WORTH_BRIDGE_BAR_CLASS,
  NET_WORTH_BRIDGE_DEGRADED_AMOUNT_CLASS,
  NET_WORTH_BRIDGE_DEGRADED_BAR_CLASS,
} from '../../constants/net-worth-bridge/net-worth-bridge.constants';
import {NetWorthBridgeStore} from '../../store/net-worth-bridge/net-worth-bridge.store';

/**
 * Bridge waterfall (finance-framework P4): how net worth got from the start of the dashboard's range to its
 * end - savings, investment return, currency moves, revaluation and accounts linked or removed (M5) - with
 * the unexplained residual always on its own row. A residual above tolerance degrades the bridge and says
 * so in place, beside the badge's reason sheet.
 */
@Component({
  selector: 'fns-net-worth-bridge',
  imports: [
    AlertComponent,
    AsyncStateComponent,
    CardComponent,
    DatePipe,
    HeadlineBadgeComponent,
    MoneyPipe,
    SkeletonComponent,
  ],
  templateUrl: './net-worth-bridge.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [NetWorthBridgeStore],
})
export class NetWorthBridgeComponent {
  public readonly store = inject(NetWorthBridgeStore);
  public readonly barClass = NET_WORTH_BRIDGE_BAR_CLASS;
  public readonly amountClass = NET_WORTH_BRIDGE_AMOUNT_CLASS;
  public readonly degradedBarClass = NET_WORTH_BRIDGE_DEGRADED_BAR_CLASS;
  public readonly degradedAmountClass = NET_WORTH_BRIDGE_DEGRADED_AMOUNT_CLASS;
}
