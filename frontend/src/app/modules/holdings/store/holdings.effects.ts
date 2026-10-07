import {inject} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {type Position} from '../models/position/position.model';
import {PositionsService} from '../services/positions.service';
import {QuotesService} from '../services/quotes.service';
import {DayChangeUtils} from '../utils/day-change.utils';

interface StoreMethods {
  setPositionsLoading(): void;
  setPositions(positions: Position[]): void;
  setPositionsError(errorCode: Nullable<string>): void;
  setDayChangePct(dayChangePctByTicker: Record<string, number>): void;
}

export function holdingsEffects(store: StoreMethods) {
  const positionsService = inject(PositionsService);
  const quotesService = inject(QuotesService);

  // A failed or partial quote fetch only leaves day-change slots on their placeholder; the
  // positions stay as they are and the previous quotes stay on screen while this refreshes.
  const loadQuotes = rxMethod<string[]>(
    pipe(
      switchMap(tickers =>
        tickers.length === 0
          ? EMPTY
          : quotesService.getQuotes(tickers).pipe(
              tap(quotes => store.setDayChangePct(DayChangeUtils.pctByTicker(quotes))),
              catchError(() => EMPTY)
            )
      )
    )
  );

  const loadPositions = rxMethod<void>(
    pipe(
      tap(() => store.setPositionsLoading()),
      switchMap(() =>
        positionsService.getPositions().pipe(
          tap(positions => {
            store.setPositions(positions);
            loadQuotes(DayChangeUtils.quotableTickers(positions));
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setPositionsError(code),
          })
        )
      )
    )
  );

  return {loadPositions};
}

export function holdingsHooks(store: ReturnType<typeof holdingsEffects>) {
  return {
    onInit: () => {
      store.loadPositions();
    },
  };
}
