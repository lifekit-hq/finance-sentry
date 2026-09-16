import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {BinanceService} from '../../services/binance.service';
import {IBKRService} from '../../services/ibkr.service';
import {RevolutXService} from '../../services/revolut-x.service';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setSummary: (summary: WealthSummaryResponse) => void;
  bumpDisconnectVersion: () => void;
}

export function accountsEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);
  const bankSyncService = inject(BankSyncService);
  const ibkrService = inject(IBKRService);
  const binanceService = inject(BinanceService);
  const revolutXService = inject(RevolutXService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        wealthService.getSummary().pipe(
          tap(summary => {
            store.setSummary(summary);
            store.setSuccess();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const disconnectMonobank = rxMethod<void>(
    pipe(
      switchMap(() =>
        bankSyncService.disconnectMonobank().pipe(
          tap(() => {
            store.bumpDisconnectVersion();
            load();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const disconnectAccount = rxMethod<string>(
    pipe(
      switchMap(accountId =>
        bankSyncService.disconnectAccount(accountId).pipe(
          tap(() => {
            store.bumpDisconnectVersion();
            load();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const disconnectInstitution = rxMethod<{provider: string; institutionId: string}>(
    pipe(
      switchMap(({provider, institutionId}) =>
        bankSyncService.disconnectInstitution(provider, institutionId).pipe(
          tap(() => {
            store.bumpDisconnectVersion();
            load();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const disconnectIBKR = rxMethod<void>(
    pipe(
      switchMap(() =>
        ibkrService.disconnect().pipe(
          tap(() => {
            store.bumpDisconnectVersion();
            load();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const disconnectBinance = rxMethod<void>(
    pipe(
      switchMap(() =>
        binanceService.disconnect().pipe(
          tap(() => {
            store.bumpDisconnectVersion();
            load();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const disconnectRevolutX = rxMethod<void>(
    pipe(
      switchMap(() =>
        revolutXService.disconnect().pipe(
          tap(() => {
            store.bumpDisconnectVersion();
            load();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    disconnectMonobank,
    disconnectAccount,
    disconnectInstitution,
    disconnectIBKR,
    disconnectBinance,
    disconnectRevolutX,
  };
}

interface HookStore {
  load: () => void;
}

export function accountsHooks(store: HookStore): void {
  store.load();
}
