import {ChangeDetectionStrategy, Component, inject, input} from '@angular/core';
import {type Params, RouterLink} from '@angular/router';
import {AsyncStateComponent, CardComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  NET_WORTH_BRIDGE_ESTIMATE_NOTE,
  NET_WORTH_BRIDGE_SUMMARY_LABELS,
} from '../../constants/net-worth-bridge/net-worth-bridge.constants';
import {NetWorthBridgeStore} from '../../store/net-worth-bridge/net-worth-bridge.store';

const SKELETON_LINES = 3;

/**
 * Home's "why it moved": the net worth bridge over the page's window read as three lines - what you
 * saved, what markets did, what exchange rates did - each with one sentence, and the change they come
 * to. The full statement, with revaluation, linked accounts and the unexplained residual, is on Breakdown.
 */
@Component({
  selector: 'fns-net-worth-why-moved',
  imports: [AsyncStateComponent, CardComponent, MoneyPipe, RouterLink, SkeletonComponent],
  templateUrl: './net-worth-why-moved.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [NetWorthBridgeStore],
})
export class NetWorthWhyMovedComponent {
  public readonly store = inject(NetWorthBridgeStore);
  /** The window in words, after the title ("Last 3 months"). */
  public readonly period = input.required<string>();
  /** Breakdown's query for the same window, behind the details link. */
  public readonly detailsParams = input<Params>({});
  public readonly breakdownRoute = AppRoute.FlowBreakdown;
  public readonly changeLabel = NET_WORTH_BRIDGE_SUMMARY_LABELS.change;
  public readonly estimateNote = NET_WORTH_BRIDGE_ESTIMATE_NOTE;
  public readonly skeletonLines = Array.from({length: SKELETON_LINES});
}
