import {inject} from '@angular/core';
import {patchState, type WritableStateSource} from '@ngrx/signals';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {ErrorUtils} from '../../../../shared/utils/error.utils';
import {INZHUR_LOGIN_FAILED} from '../../constants/inzhur/inzhur.constants';
import {type StartInzhurLoginRequest} from '../../models/inzhur/inzhur.model';
import {InzhurService} from '../../services/inzhur.service';
import {AccountsStore} from '../accounts/accounts.store';
import {ConnectStore} from '../connect/connect.store';
import {type InzhurConnectState} from './inzhur-connect.state';

export function inzhurConnectEffects(store: WritableStateSource<InzhurConnectState>) {
  const inzhur = inject(InzhurService);
  const connectStore = inject(ConnectStore, {optional: true});
  const accountsStore = inject(AccountsStore, {optional: true});

  /** Inzhur signed in without asking for a code: the connection is live already. */
  const finish = (): void => {
    connectStore?.selectProvider('inzhur');
    connectStore?.setInstitutionType('broker');
    connectStore?.setSuccess();
    accountsStore?.load();
  };

  const loadStatus = rxMethod<void>(
    pipe(
      tap(() => patchState(store, {statusLoad: 'loading'})),
      switchMap(() =>
        inzhur.getStatus().pipe(
          tap(connection => patchState(store, {connection, statusLoad: 'loaded'})),
          catchError(() => {
            patchState(store, {statusLoad: 'error'});
            return EMPTY;
          })
        )
      )
    )
  );

  // exhaustMap: a double tap must never start a second sign-in (and a second SMS).
  const start = rxMethod<StartInzhurLoginRequest>(
    pipe(
      tap(() => patchState(store, {starting: true, errorCode: null})),
      exhaustMap(request =>
        inzhur.startLogin(request).pipe(
          tap(result => {
            patchState(store, {starting: false});
            if (result.status === 'connected') {
              finish();
              return;
            }
            patchState(store, {step: 'code', codeExpiresAt: result.codeExpiresAt});
          }),
          catchError((err: unknown) => {
            // A failure without a code (network, 5xx page) still has to be shown.
            patchState(store, {
              starting: false,
              errorCode: ErrorUtils.extractCode(err) ?? INZHUR_LOGIN_FAILED,
            });
            return EMPTY;
          })
        )
      )
    )
  );

  return {loadStatus, start};
}

export function inzhurConnectHooks(store: {loadStatus: () => void}): void {
  store.loadStatus();
}
