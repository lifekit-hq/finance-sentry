import {inject, type Signal} from '@angular/core';
import {ActivatedRoute, type ParamMap} from '@angular/router';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {distinctUntilChanged, filter, map, pipe, switchMap, tap} from 'rxjs';

import {ASSET_DOSSIER_SYMBOL_PARAM} from '../../../shared/enums/app-route/app-route.enum';
import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {AuthStore} from '../../auth/store/auth.store';
import {
  type AssetPriceBarsDto,
  type MovingAverageId,
  type PriceChartKind,
  type PriceIntervalId,
  type PricePeriodId,
} from '../models/price-chart/price-chart.model';
import {DossierService} from '../services/dossier.service';
import {PriceChartSelectionUtils} from '../utils/price-chart-selection.utils';

interface StoreMethods {
  resetForSymbol(symbol: string): void;
  setBarsLoading(): void;
  setBarsSuccess(): void;
  setBarsError(errorCode: Nullable<string>): void;
  setBarsIdle(): void;
  setBars(bars: AssetPriceBarsDto): void;
  setPeriod(period: PricePeriodId): void;
  setInterval(interval: PriceIntervalId): void;
  setKind(kind: PriceChartKind): void;
  setMovingAverages(movingAverages: MovingAverageId[]): void;
  symbol: Signal<Nullable<string>>;
  period: Signal<PricePeriodId>;
  interval: Signal<Nullable<PriceIntervalId>>;
  kind: Signal<PriceChartKind>;
  movingAverages: Signal<MovingAverageId[]>;
}

export function priceChartEffects(store: StoreMethods) {
  const dossierService = inject(DossierService);
  const authStore = inject(AuthStore);

  // switchMap drops a period or candle size the reader already moved past, so a slow response never lands over a newer pick.
  const loadBars = rxMethod<{
    symbol: string;
    period: PricePeriodId;
    interval: Nullable<PriceIntervalId>;
  }>(
    pipe(
      tap(() => store.setBarsLoading()),
      switchMap(({symbol, period, interval}) =>
        dossierService.getBars(symbol, period, interval).pipe(
          tap(bars => {
            store.setBars(bars);
            store.setBarsSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setBarsError(code),
          })
        )
      )
    )
  );

  const reload = (): void => {
    const symbol = store.symbol();
    if (symbol) {
      loadBars({symbol, period: store.period(), interval: store.interval()});
    }
  };

  const openSymbol = (symbol: string): void => {
    store.setBarsIdle();
    store.resetForSymbol(symbol);
    reload();
  };

  const selectPeriod = (period: PricePeriodId): void => {
    if (period === store.period()) {
      return;
    }
    store.setPeriod(period);
    reload();
  };

  const selectInterval = (interval: PriceIntervalId): void => {
    if (interval === store.interval()) {
      return;
    }
    store.setInterval(interval);
    reload();
  };

  const persist = (): void =>
    PriceChartSelectionUtils.write(authStore.userId(), {
      kind: store.kind(),
      movingAverages: store.movingAverages(),
    });

  const selectKind = (kind: PriceChartKind): void => {
    store.setKind(kind);
    persist();
  };

  const toggleMovingAverage = (id: MovingAverageId): void => {
    store.setMovingAverages(PriceChartSelectionUtils.toggle(store.movingAverages(), id));
    persist();
  };

  const restoreSelection = (): void => {
    const saved = PriceChartSelectionUtils.read(authStore.userId());
    store.setKind(saved.kind);
    store.setMovingAverages(saved.movingAverages);
  };

  return {
    openSymbol,
    reload,
    selectPeriod,
    selectInterval,
    selectKind,
    toggleMovingAverage,
    restoreSelection,
  };
}

export function priceChartHooks(store: ReturnType<typeof priceChartEffects>) {
  const route = inject(ActivatedRoute);

  // Follows the route's paramMap, like the dossier it sits on, so a symbol change in place reloads the chart.
  const followSymbol = rxMethod<ParamMap>(
    pipe(
      map(params => params.get(ASSET_DOSSIER_SYMBOL_PARAM) ?? ''),
      filter(symbol => symbol !== ''),
      distinctUntilChanged(),
      tap(symbol => store.openSymbol(symbol))
    )
  );

  return {
    onInit: () => {
      store.restoreSelection();
      followSymbol(route.paramMap);
    },
  };
}
