import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {priceChartComputed} from './price-chart.computed';
import {priceChartEffects, priceChartHooks} from './price-chart.effects';
import {priceChartMethods} from './price-chart.methods';
import {initialPriceChartState} from './price-chart.state';

export const PriceChartStore = signalStore(
  withState(initialPriceChartState),
  withNamedAsyncStatus('bars', {defaultErrorMessage: 'Failed to load the price chart.'}),
  withMethods(priceChartMethods),
  withComputed(priceChartComputed),
  withMethods(priceChartEffects),
  withHooks(priceChartHooks)
);
