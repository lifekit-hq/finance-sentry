import {inject, InjectionToken, type Provider, type Signal} from '@angular/core';

import {type DateRange} from '../../../shared/utils/date-range.utils';
import {DashboardStore} from '../store/dashboard/dashboard.store';
import {FlowBreakdownStore} from '../store/flow-breakdown/flow-breakdown.store';

/**
 * The days the net worth bridge explains. Each page that draws a bridge provides the window it
 * shows: Home its range chips, Breakdown the window or month on screen.
 */
export const NET_WORTH_BRIDGE_WINDOW = new InjectionToken<Signal<DateRange>>(
  'NetWorthBridgeWindow'
);

export function provideDashboardBridgeWindow(): Provider {
  return {provide: NET_WORTH_BRIDGE_WINDOW, useFactory: () => inject(DashboardStore).bridgeWindow};
}

export function provideBreakdownBridgeWindow(): Provider {
  return {
    provide: NET_WORTH_BRIDGE_WINDOW,
    useFactory: () => inject(FlowBreakdownStore).bridgeWindow,
  };
}
