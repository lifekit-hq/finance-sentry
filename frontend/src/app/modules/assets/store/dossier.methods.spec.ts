import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type AssetDossierDto} from '../models/dossier/dossier.model';
import {dossierMethods} from './dossier.methods';
import {initialDossierState} from './dossier.state';

function mkDossier(overrides: Partial<AssetDossierDto> = {}): AssetDossierDto {
  return {
    symbol: 'AAPL',
    position: null,
    thesis: null,
    valuation: null,
    analysts: null,
    recentNews: [],
    nextEarnings: null,
    radarSignals: [],
    generatedAt: '2026-09-01T10:00:00Z',
    ...overrides,
  };
}

describe('dossierMethods', () => {
  it('setDossierLoading transitions to loading and clears error', () => {
    const state = signalState({
      ...initialDossierState,
      dossierStatus: 'error' as const,
      dossierErrorCode: 'SOME_ERROR',
    });
    const methods = dossierMethods(state);

    methods.setDossierLoading();

    expect(state.dossierStatus()).toBe('loading');
    expect(state.dossierErrorCode()).toBeNull();
  });

  it('setDossier stores the dossier and resets status to idle', () => {
    const state = signalState({...initialDossierState, dossierStatus: 'loading' as const});
    const methods = dossierMethods(state);
    const dossier = mkDossier({symbol: 'MSFT'});

    methods.setDossier(dossier);

    expect(state.dossier()).toEqual(dossier);
    expect(state.dossierStatus()).toBe('idle');
    expect(state.dossierErrorCode()).toBeNull();
  });

  it('setDossierError stores the error code and sets error status', () => {
    const state = signalState({...initialDossierState, dossierStatus: 'loading' as const});
    const methods = dossierMethods(state);

    methods.setDossierError('DOSSIER_NOT_FOUND');

    expect(state.dossierStatus()).toBe('error');
    expect(state.dossierErrorCode()).toBe('DOSSIER_NOT_FOUND');
  });

  it('setDossierError accepts null error code', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);

    methods.setDossierError(null);

    expect(state.dossierStatus()).toBe('error');
    expect(state.dossierErrorCode()).toBeNull();
  });

  it('setDossierLoading clears any previously loaded dossier status', () => {
    const state = signalState({...initialDossierState, dossierStatus: 'idle' as const});
    const methods = dossierMethods(state);
    methods.setDossier(mkDossier());

    methods.setDossierLoading();

    expect(state.dossierStatus()).toBe('loading');
    expect(state.dossier()).not.toBeNull();
  });

  it('setLedgerRead stores the read and resets status to idle', () => {
    const state = signalState({...initialDossierState, ledgerReadStatus: 'loading' as const});
    const methods = dossierMethods(state);
    const read = {
      symbol: 'AAPL',
      narrative: 'A read.',
      generatedAt: '2026-09-03T00:00:00Z',
      isStale: false,
      cached: true,
    };

    methods.setLedgerRead(read);

    expect(state.ledgerRead()).toEqual(read);
    expect(state.ledgerReadStatus()).toBe('idle');
    expect(state.ledgerReadErrorCode()).toBeNull();
  });

  it('setLedgerReadLoading clears a previous error but keeps the cached read visible', () => {
    const state = signalState({
      ...initialDossierState,
      ledgerReadStatus: 'error' as const,
      ledgerReadErrorCode: 'LEDGER_READ_UNAVAILABLE',
    });
    const methods = dossierMethods(state);
    methods.setLedgerRead({
      symbol: 'AAPL',
      narrative: 'Stays put.',
      generatedAt: null,
      isStale: true,
      cached: true,
    });

    methods.setLedgerReadLoading();

    expect(state.ledgerReadStatus()).toBe('loading');
    expect(state.ledgerReadErrorCode()).toBeNull();
    expect(state.ledgerRead()?.narrative).toBe('Stays put.');
  });

  it('setLedgerReadError stores the error code and sets error status', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);

    methods.setLedgerReadError('LEDGER_READ_UNAVAILABLE');

    expect(state.ledgerReadStatus()).toBe('error');
    expect(state.ledgerReadErrorCode()).toBe('LEDGER_READ_UNAVAILABLE');
  });

  it('toggleThesisExpanded flips the flag', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);

    methods.toggleThesisExpanded();
    expect(state.isThesisExpanded()).toBe(true);

    methods.toggleThesisExpanded();
    expect(state.isThesisExpanded()).toBe(false);
  });

  it('setQuote stores the quote and resets status to idle', () => {
    const state = signalState({...initialDossierState, quoteStatus: 'loading' as const});
    const quote = {
      ticker: 'AAPL',
      price: 189.3,
      previousClose: 187,
      changePct: 1.23,
      currency: 'USD',
    };

    dossierMethods(state).setQuote(quote);

    expect(state.quote()).toEqual(quote);
    expect(state.quoteStatus()).toBe('idle');
  });

  it('setQuoteError clears the quote and never touches the dossier status', () => {
    const state = signalState({...initialDossierState, dossierStatus: 'idle' as const});

    dossierMethods(state).setQuoteError();

    expect(state.quote()).toBeNull();
    expect(state.quoteStatus()).toBe('error');
    expect(state.dossierStatus()).toBe('idle');
  });
});
