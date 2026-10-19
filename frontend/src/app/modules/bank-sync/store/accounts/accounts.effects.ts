import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, forkJoin, of, pipe, switchMap, tap} from 'rxjs';

import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {
  type ManualHolding,
  type ManualHoldingInput,
} from '../../models/manual-holding/manual-holding.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {BinanceService} from '../../services/binance.service';
import {IBKRService} from '../../services/ibkr.service';
import {InzhurService} from '../../services/inzhur.service';
import {RevolutXService} from '../../services/revolut-x.service';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setSummary: (summary: WealthSummaryResponse) => void;
  setAllAccounts: (accounts: BankAccount[]) => void;
  setManualHoldings: (holdings: ManualHolding[]) => void;
  bumpDisconnectVersion: () => void;
}

export function accountsEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);
  const bankSyncService = inject(BankSyncService);
  const ibkrService = inject(IBKRService);
  const inzhurService = inject(InzhurService);
  const binanceService = inject(BinanceService);
  const revolutXService = inject(RevolutXService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        forkJoin([
          wealthService.getSummary(),
          // The closed section and the row notes are extras: a failed list leaves what was there, never the page.
          bankSyncService.getAccounts().pipe(catchError(() => of(null))),
          wealthService.getManualHoldings().pipe(catchError(() => of(null))),
        ]).pipe(
          tap(([summary, accounts, manualHoldings]) => {
            store.setSummary(summary);
            if (accounts) {
              store.setAllAccounts(accounts.accounts);
            }
            if (manualHoldings) {
              store.setManualHoldings(manualHoldings);
            }
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

  // A retired account counts again from the next sync; the reload shows it back in its card's group.
  const reopenAccount = rxMethod<string>(
    pipe(
      switchMap(accountId =>
        bankSyncService.reopenAccount(accountId).pipe(
          tap(() => load()),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  // The reload brings the new value into the total, the breakdown and the section together.
  const saveManualHolding = rxMethod<{id: Nullable<string>; input: ManualHoldingInput}>(
    pipe(
      switchMap(({id, input}) =>
        (id
          ? wealthService.updateManualHolding(id, input)
          : wealthService.createManualHolding(input)
        ).pipe(
          tap(() => load()),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  const removeManualHolding = rxMethod<string>(
    pipe(
      switchMap(id =>
        wealthService.deleteManualHolding(id).pipe(
          tap(() => load()),
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

  const disconnectInzhur = rxMethod<void>(
    pipe(
      switchMap(() =>
        inzhurService.disconnect().pipe(
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
    reopenAccount,
    saveManualHolding,
    removeManualHolding,
    disconnectMonobank,
    disconnectAccount,
    disconnectInstitution,
    disconnectIBKR,
    disconnectInzhur,
    disconnectBinance,
    disconnectRevolutX,
  };
}

interface HookStore {
  load: () => void;
  restoreInstitutionOpen: () => void;
}

export function accountsHooks(store: HookStore): void {
  store.restoreInstitutionOpen();
  store.load();
}
