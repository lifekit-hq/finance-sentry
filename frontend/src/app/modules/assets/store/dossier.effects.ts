import {DOCUMENT} from '@angular/common';
import {afterNextRender, inject, Injector} from '@angular/core';
import {ActivatedRoute} from '@angular/router';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {ASSET_DOSSIER_SYMBOL_PARAM} from '../../../shared/enums/app-route/app-route.enum';
import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {AuthStore} from '../../auth/store/auth.store';
import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';
import {DossierService} from '../services/dossier.service';
import {DossierQuoteUtils} from '../utils/dossier-quote.utils';

interface StoreMethods {
  setDossierLoading(): void;
  setDossier(dossier: AssetDossierDto): void;
  setDossierError(errorCode: Nullable<string>): void;
  setLedgerReadLoading(): void;
  setLedgerRead(ledgerRead: AssetLedgerReadDto): void;
  setLedgerReadError(errorCode: Nullable<string>): void;
  setQuoteLoading(): void;
  setQuote(quote: Nullable<DossierQuoteDto>): void;
  setQuoteError(): void;
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

  // Runs once the dossier says the symbol is a listed equity; a failed quote never breaks the page.
  const loadQuote = rxMethod<string>(
    pipe(
      tap(() => store.setQuoteLoading()),
      switchMap(symbol =>
        dossierService.getQuotes(symbol).pipe(
          tap(quotes => store.setQuote(quotes[0] ?? null)),
          catchError(() => {
            store.setQuoteError();
            return EMPTY;
          })
        )
      )
    )
  );

  const loadDossier = rxMethod<string>(
    pipe(
      tap(() => store.setDossierLoading()),
      switchMap(symbol =>
        dossierService.getDossier(symbol).pipe(
          tap(dossier => {
            store.setDossier(dossier);
            if (DossierQuoteUtils.isQuotable(dossier)) {
              loadQuote(dossier.symbol);
            }
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
