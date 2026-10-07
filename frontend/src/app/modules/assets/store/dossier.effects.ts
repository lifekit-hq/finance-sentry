import {DOCUMENT} from '@angular/common';
import {afterNextRender, inject, Injector} from '@angular/core';
import {ActivatedRoute} from '@angular/router';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {ASSET_DOSSIER_SYMBOL_PARAM} from '../../../shared/enums/app-route/app-route.enum';
import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {AuthStore} from '../../auth/store/auth.store';
import {type AssetDossierDto, type AssetLedgerReadDto} from '../models/dossier/dossier.model';
import {DossierService} from '../services/dossier.service';

interface StoreMethods {
  setDossierLoading(): void;
  setDossier(dossier: AssetDossierDto): void;
  setDossierError(errorCode: Nullable<string>): void;
  setLedgerReadLoading(): void;
  setLedgerRead(ledgerRead: AssetLedgerReadDto): void;
  setLedgerReadError(errorCode: Nullable<string>): void;
}

export function dossierEffects(store: StoreMethods) {
  const dossierService = inject(DossierService);
  const route = inject(ActivatedRoute);
  const document = inject(DOCUMENT);
  const injector = inject(Injector);

  // An alert deep-links to a card (`/assets/NVDA#analyst-coverage`); the card only exists once the
  // dossier has loaded, so the router's own fragment handling has nothing to land on. Scroll after
  // the render that follows the dossier arriving.
  const scrollToFragment = () => {
    const fragment = route.snapshot.fragment;
    if (!fragment) {
      return;
    }
    afterNextRender(() => document.getElementById(fragment)?.scrollIntoView({block: 'start'}), {
      injector,
    });
  };

  const loadDossier = rxMethod<string>(
    pipe(
      tap(() => store.setDossierLoading()),
      switchMap(symbol =>
        dossierService.getDossier(symbol).pipe(
          tap(dossier => {
            store.setDossier(dossier);
            scrollToFragment();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setDossierError(code),
          })
        )
      )
    )
  );

  // Cached-only fetch — runs on page load so a previously generated read renders instantly.
  const loadLedgerRead = rxMethod<string>(
    pipe(
      tap(() => store.setLedgerReadLoading()),
      switchMap(symbol =>
        dossierService.getLedgerRead(symbol).pipe(
          tap(read => store.setLedgerRead(read)),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setLedgerReadError(code),
          })
        )
      )
    )
  );

  const generateLedgerRead = rxMethod<{symbol: string; force: boolean}>(
    pipe(
      tap(() => store.setLedgerReadLoading()),
      switchMap(({symbol, force}) =>
        dossierService.generateLedgerRead(symbol, force).pipe(
          tap(read => store.setLedgerRead(read)),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setLedgerReadError(code),
          })
        )
      )
    )
  );

  return {loadDossier, loadLedgerRead, generateLedgerRead};
}

export function dossierHooks(store: ReturnType<typeof dossierEffects>) {
  const route = inject(ActivatedRoute);
  const authStore = inject(AuthStore);

  return {
    onInit: () => {
      const symbol = route.snapshot.paramMap.get(ASSET_DOSSIER_SYMBOL_PARAM) ?? '';
      if (symbol) {
        store.loadDossier(symbol);
        if (authStore.canUseAi()) {
          store.loadLedgerRead(symbol);
        }
      }
    },
  };
}
