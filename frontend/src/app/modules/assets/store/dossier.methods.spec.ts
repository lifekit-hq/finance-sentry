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
  it('setDossier stores the dossier', () => {
    const state = signalState(initialDossierState);
    const dossier = mkDossier({symbol: 'MSFT'});

    dossierMethods(state).setDossier(dossier);

    expect(state.dossier()).toEqual(dossier);
  });

  it('setLedgerRead stores the read', () => {
    const state = signalState(initialDossierState);
    const read = {
      symbol: 'AAPL',
      narrative: 'A read.',
      generatedAt: '2026-09-03T00:00:00Z',
      isStale: false,
      cached: true,
    };

    dossierMethods(state).setLedgerRead(read);

    expect(state.ledgerRead()).toEqual(read);
  });

  it('toggleThesisExpanded flips the flag', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);

    methods.toggleThesisExpanded();
    expect(state.isThesisExpanded()).toBe(true);

    methods.toggleThesisExpanded();
    expect(state.isThesisExpanded()).toBe(false);
  });

  it('setQuote stores the quote and clearQuote empties it', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);
    const quote = {
      ticker: 'AAPL',
      price: 189.3,
      previousClose: 187,
      changePct: 1.23,
      currency: 'USD',
    };

    methods.setQuote(quote);
    expect(state.quote()).toEqual(quote);

    methods.clearQuote();
    expect(state.quote()).toBeNull();
  });
});
