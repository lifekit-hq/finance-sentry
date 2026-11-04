import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {type VerdictWord} from '../../../shared/models/scorecard/scorecard.model';
import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {AuthStore} from '../../auth/store/auth.store';
import {type Position} from '../models/position/position.model';
import {PositionsService} from '../services/positions.service';
import {QuotesService} from '../services/quotes.service';
import {VerdictsService} from '../services/verdicts.service';
import {DayChangeUtils} from '../utils/day-change.utils';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setPositions(positions: Position[]): void;
  setHeadline(headline: Nullable<Headline>): void;
  setDayChangePct(dayChangePctByTicker: Record<string, number>): void;
  setVerdicts(verdictByTicker: Record<string, VerdictWord>): void;
}

export function holdingsEffects(store: StoreMethods) {
  const positionsService = inject(PositionsService);
  const quotesService = inject(QuotesService);
  const verdictsService = inject(VerdictsService);
  const authStore = inject(AuthStore);

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

  // The verdict chips (spec 726 US2): one batch read for the held listed stocks, owner only. A failed read
  // leaves the chips as they were; a row without a word simply shows no chip.
  const loadVerdicts = rxMethod<string[]>(
    pipe(
      switchMap(tickers =>
        tickers.length === 0
          ? EMPTY
          : verdictsService.getVerdicts(tickers).pipe(
              tap(verdicts =>
                store.setVerdicts(
                  Object.fromEntries(verdicts.map(v => [v.symbol.toUpperCase(), v.word]))
                )
              ),
              catchError(() => EMPTY)
            )
      )
    )
  );

  const loadPositions = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        positionsService.getHoldings().pipe(
          tap(({positions, headline}) => {
            store.setPositions(positions);
            store.setHeadline(headline);
            store.setSuccess();
            const tickers = DayChangeUtils.quotableTickers(positions);
            loadQuotes(tickers);
            if (authStore.canUseAi()) {
              loadVerdicts(tickers);
            }
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setError(code),
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
