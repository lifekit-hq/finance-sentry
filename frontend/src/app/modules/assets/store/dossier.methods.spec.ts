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

  it('setChart and setChartRange store the chart and its range', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);
    const chart = {
      symbol: 'AAPL',
      range: '6M' as const,
      currency: 'USD',
      points: [],
      benchmarkLabel: 'S&P 500',
      trades: [],
      averageCost: null,
      changePct: null,
      benchmarkChangePct: null,
      source: 'yahoo-chart',
      retrievedAt: '2026-10-13T00:00:00Z',
    };

    methods.setChart(chart);
    methods.setChartRange('6M');

    expect(state.chart()).toEqual(chart);
    expect(state.chartRange()).toBe('6M');
  });

  it('openTargetEditor starts from the saved target, or blank without one', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);

    methods.openTargetEditor();
    expect(state.isEditingTarget()).toBe(true);
    expect(state.targetDraft()).toBe('');

    methods.setDossier(
      mkDossier({
        userTarget: {targetPrice: 250.5, currency: 'USD', updatedAt: '2026-10-13T00:00:00Z'},
      })
    );
    methods.openTargetEditor();
    expect(state.targetDraft()).toBe('250.5');
  });

  it('setTargetDraft stores the text and closeTargetEditor drops it', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);

    methods.openTargetEditor();
    methods.setTargetDraft('99');
    expect(state.targetDraft()).toBe('99');

    methods.closeTargetEditor();
    expect(state.isEditingTarget()).toBe(false);
    expect(state.targetDraft()).toBe('');
  });

  it('setUserTarget writes the target into the dossier and closes the editor', () => {
    const state = signalState(initialDossierState);
    const methods = dossierMethods(state);
    const target = {targetPrice: 300, currency: 'USD', updatedAt: '2026-10-13T00:00:00Z'};

    methods.setUserTarget(target);
    expect(state.dossier()).toBeNull();

    methods.setDossier(mkDossier());
    methods.openTargetEditor();
    methods.setUserTarget(target);

    expect(state.dossier()?.userTarget).toEqual(target);
    expect(state.isEditingTarget()).toBe(false);

    methods.setUserTarget(null);
    expect(state.dossier()?.userTarget).toBeNull();
  });
});
