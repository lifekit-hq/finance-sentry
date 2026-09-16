import {effect, inject} from '@angular/core';
import {Router} from '@angular/router';
import {BankSyncService} from '@modules/bank-sync/services/bank-sync.service';
import {ConnectStrategy} from '@modules/bank-sync/strategies/connect-strategy';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, map, pipe, race, switchMap, take, tap, timer} from 'rxjs';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type InstitutionType,
  type Provider,
} from '../../../../shared/models/provider/provider.model';
import {ErrorUtils} from '../../../../shared/utils/error.utils';
import {AccountsStore} from '../accounts/accounts.store';
import {type ConnectStatus} from './connect.state';

interface EffectsStore {
  setSyncing: (msg: string) => void;
  setPolling: (msg: string) => void;
  setSuccess: () => void;
  setError: (code: Nullable<string>) => void;
  setInstitutionType: (type: InstitutionType) => void;
  selectProvider: (provider: Provider) => void;
  status: () => ConnectStatus;
  institutionType: () => Nullable<InstitutionType>;
}

interface ConnectInput {
  readonly strategy: ConnectStrategy;
  readonly payload: unknown;
}

const POLL_INTERVAL_MS = 3000;
const POLL_MAX_MS = 60_000;

const ROUTE_BY_INSTITUTION: Record<InstitutionType, string> = {
  bank: AppRoute.Accounts,
  crypto: AppRoute.AccountsInvestments,
  broker: AppRoute.AccountsInvestments,
};

function extractCode(err: unknown): Nullable<string> {
  const direct = (err as {errorCode?: string}).errorCode;
  if (direct) {
    return direct;
  }
  return ErrorUtils.extractCode(err);
}

function institutionTypeForSlug(strategy: ConnectStrategy): InstitutionType {
  switch (strategy.slug) {
    case 'monobank':
    case 'truelayer':
      return 'bank';
    case 'binance':
    case 'revolut_x':
      return 'crypto';
    case 'ibkr':
      return 'broker';
  }
}

export function connectEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);
  const accountsStore = inject(AccountsStore, {optional: true});

  const pollForActive = rxMethod<void>(
    pipe(
      tap(() => store.setPolling('Syncing transaction history...')),
      switchMap(() => {
        const polling$ = timer(0, POLL_INTERVAL_MS).pipe(
          switchMap(() => bankSyncService.getAccounts()),
          filter(res =>
            res.accounts.some(a => a.syncStatus === 'active' || a.syncStatus === 'syncing')
          ),
          take(1),
          map(() => 'active' as const)
        );
        const timeout$ = timer(POLL_MAX_MS).pipe(map(() => 'timeout' as const));
        return race(polling$, timeout$).pipe(
          tap(() => {
            store.setSuccess();
            accountsStore?.load();
          }),
          catchError((err: unknown) => {
            store.setError(extractCode(err));
            return EMPTY;
          })
        );
      })
    )
  );

  const connect = rxMethod<ConnectInput>(
    pipe(
      tap(({strategy}) => {
        store.selectProvider(strategy.slug);
        store.setInstitutionType(institutionTypeForSlug(strategy));
        store.setSyncing(`Connecting your ${strategy.slug} account...`);
      }),
      switchMap(({strategy, payload}) =>
        strategy.submit(payload).pipe(
          tap(outcome => {
            if (outcome.successCode === 'POLLING' && outcome.institutionType === 'bank') {
              pollForActive();
            } else {
              store.setSuccess();
              accountsStore?.load();
            }
          }),
          catchError((err: unknown) => {
            store.setError(extractCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {connect, pollForActive};
}

interface SuccessHookStore {
  status: () => ConnectStatus;
  institutionType: () => Nullable<InstitutionType>;
}

export function connectSuccessRouter(store: SuccessHookStore): void {
  const router = inject(Router);
  effect(() => {
    if (store.status() !== 'success') {
      return;
    }
    const type = store.institutionType();
    if (!type) {
      return;
    }
    const target = ROUTE_BY_INSTITUTION[type];
    void router.navigateByUrl(target);
  });
}
