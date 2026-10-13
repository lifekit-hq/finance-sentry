import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {AuthStore} from '../../auth/store/auth.store';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
  type DossierUserTargetDto,
} from '../models/dossier/dossier.model';
import {DossierService} from '../services/dossier.service';
import {DossierStore} from './dossier.store';

const DOSSIER: AssetDossierDto = {
  symbol: 'AAPL',
  position: {
    provider: 'ibkr',
    assetClass: 'Equities',
    quantity: 1,
    currentValueUsd: 100,
    costBasisUsd: null,
    unrealizedPnlUsd: null,
    unrealizedPnlPercent: null,
    shareOfBookPercent: null,
    taxLots: [],
  },
  thesis: null,
  valuation: null,
  analysts: null,
  recentNews: [],
  nextEarnings: null,
  radarSignals: [],
  generatedAt: '2026-09-01T10:00:00Z',
};

const READ: AssetLedgerReadDto = {
  symbol: 'AAPL',
  narrative: 'A read.',
  generatedAt: '2026-09-03T00:00:00Z',
  isStale: false,
  cached: true,
};

const QUOTE: DossierQuoteDto = {
  ticker: 'AAPL',
  price: 189.3,
  previousClose: 187,
  changePct: 1.23,
  currency: 'USD',
};

const CHART: AssetChartDto = {
  symbol: 'AAPL',
  range: '1Y',
  currency: 'USD',
  points: [{date: '2026-10-13', close: 189.3, benchmark: 100}],
  benchmarkLabel: 'S&P 500',
  trades: [],
  averageCost: null,
  changePct: 1,
  benchmarkChangePct: 1,
  source: 'yahoo-chart',
  retrievedAt: '2026-10-13T00:00:00Z',
};

const TARGET: DossierUserTargetDto = {
  targetPrice: 250,
  currency: 'USD',
  updatedAt: '2026-10-13T00:00:00Z',
};

function httpError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 500, error: {errorCode}}));
}

describe('DossierStore', () => {
  let service: {
    getDossier: ReturnType<typeof vi.fn>;
    getQuotes: ReturnType<typeof vi.fn>;
    getLedgerRead: ReturnType<typeof vi.fn>;
    generateLedgerRead: ReturnType<typeof vi.fn>;
    getChart: ReturnType<typeof vi.fn>;
    setTarget: ReturnType<typeof vi.fn>;
    clearTarget: ReturnType<typeof vi.fn>;
  };
  let canUseAi: boolean;

  function inject(): InstanceType<typeof DossierStore> {
    TestBed.configureTestingModule({
      providers: [
        DossierStore,
        {provide: DossierService, useValue: service},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {
          provide: ActivatedRoute,
          useValue: {snapshot: {paramMap: convertToParamMap({symbol: 'AAPL'}), fragment: null}},
        },
        {provide: AuthStore, useValue: {canUseAi: () => canUseAi}},
      ],
    });
    return TestBed.inject(DossierStore);
  }

  beforeEach(() => {
    canUseAi = true;
    service = {
      getDossier: vi.fn().mockReturnValue(of(DOSSIER)),
      getQuotes: vi.fn().mockReturnValue(of([QUOTE])),
      getLedgerRead: vi.fn().mockReturnValue(of(READ)),
      generateLedgerRead: vi.fn().mockReturnValue(of(READ)),
      getChart: vi.fn().mockImplementation((_: string, range: string) => of({...CHART, range})),
      setTarget: vi.fn().mockReturnValue(of(TARGET)),
      clearTarget: vi.fn().mockReturnValue(of(undefined)),
    };
  });

  it('is loading until the dossier arrives', () => {
    service.getDossier.mockReturnValue(NEVER);

    const store = inject();

    expect(store.dossierStatus()).toBe('loading');
    expect(store.isDossierLoading()).toBe(true);
  });

  it('reports success for all three requests once everything loaded', () => {
    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.ledgerReadStatus()).toBe('success');
    expect(store.quoteStatus()).toBe('success');
    expect(store.dossier()).toEqual(DOSSIER);
    expect(store.ledgerRead()).toEqual(READ);
    expect(store.quoteHeader()?.priceText).toBe('$189.30');
  });

  it('keeps the quote and ledger read idle when they are not requested', () => {
    canUseAi = false;
    service.getDossier.mockReturnValue(of({...DOSSIER, position: null}));

    const store = inject();

    expect(store.ledgerReadStatus()).toBe('idle');
    expect(store.quoteStatus()).toBe('idle');
    expect(service.getLedgerRead).not.toHaveBeenCalled();
    expect(service.getQuotes).not.toHaveBeenCalled();
  });

  it('shows the default message for an unregistered dossier error code', () => {
    service.getDossier.mockReturnValue(httpError('UNKNOWN'));

    const store = inject();

    expect(store.dossierStatus()).toBe('error');
    expect(store.dossierErrorMessage()).toBe('Failed to load asset dossier.');
  });

  it('keeps a failed ledger read from touching the dossier and resolves its message', () => {
    service.getLedgerRead.mockReturnValue(httpError('LEDGER_READ_UNAVAILABLE'));

    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.dossierErrorMessage()).toBe('');
    expect(store.ledgerReadErrorMessage()).toBe(
      'Ledger could not produce a read right now. Try again shortly.'
    );
  });

  it('falls back to the default message for an unregistered ledger read code', () => {
    service.getLedgerRead.mockReturnValue(httpError('NOPE_XYZ'));

    const store = inject();

    expect(store.ledgerReadErrorMessage()).toBe("Failed to load Ledger's read.");
  });

  it('clears the quote and flags the error without raising a dossier error when the quote fails', () => {
    service.getQuotes.mockReturnValue(throwError(() => new Error('boom')));

    const store = inject();

    expect(store.quote()).toBeNull();
    expect(store.quoteStatus()).toBe('error');
    expect(store.dossierStatus()).toBe('success');
    expect(store.dossierErrorMessage()).toBe('');
  });

  it('keeps the cached read visible while a regeneration is in flight', () => {
    const store = inject();
    service.generateLedgerRead.mockReturnValue(NEVER);

    store.generateLedgerRead({symbol: 'AAPL', force: true});

    expect(store.isLedgerReadLoading()).toBe(true);
    expect(store.ledgerRead()).toEqual(READ);
  });

  it('loads the one-year chart with the dossier and reloads on a new range only', () => {
    const store = inject();

    expect(service.getChart).toHaveBeenCalledWith('AAPL', '1Y');
    expect(store.chartStatus()).toBe('success');
    expect(store.chart()?.range).toBe('1Y');

    store.selectChartRange('1Y');
    expect(service.getChart).toHaveBeenCalledTimes(1);

    store.selectChartRange('5Y');
    expect(service.getChart).toHaveBeenLastCalledWith('AAPL', '5Y');
    expect(store.chartRange()).toBe('5Y');
    expect(store.chart()?.range).toBe('5Y');
  });

  it('keeps the page when the chart fails and resolves its message', () => {
    service.getChart.mockReturnValue(httpError('INVALID_CHART_RANGE'));

    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.chartStatus()).toBe('error');
    expect(store.chartAsyncStatus()).toBe('error');
  });

  it('ignores a range change before the dossier has loaded', () => {
    service.getDossier.mockReturnValue(NEVER);
    const store = inject();

    store.selectChartRange('5Y');

    expect(service.getChart).not.toHaveBeenCalled();
    expect(store.chartRange()).toBe('1Y');
  });

  it('saves a valid draft in the asset currency and writes it into the dossier', () => {
    service.getDossier.mockReturnValue(
      of({
        ...DOSSIER,
        performance: {
          lastClose: 189,
          yearChangePct: null,
          high52Week: null,
          low52Week: null,
          fromHighPct: null,
          benchmarkYearChangePct: null,
          currency: 'EUR',
          asOf: null,
        },
      })
    );
    const store = inject();

    store.openTargetEditor();
    store.setTargetDraft('250');
    store.saveTarget();

    expect(service.setTarget).toHaveBeenCalledWith('AAPL', {targetPrice: 250, currency: 'EUR'});
    expect(store.dossier()?.userTarget).toEqual(TARGET);
    expect(store.isEditingTarget()).toBe(false);
    expect(store.targetStatus()).toBe('success');
  });

  it('sends no currency when the asset has none on record', () => {
    const store = inject();

    store.setTargetDraft('250');
    store.saveTarget();

    expect(service.setTarget).toHaveBeenCalledWith('AAPL', {targetPrice: 250, currency: null});
  });

  it('never sends an invalid draft', () => {
    const store = inject();

    store.setTargetDraft('-1');
    store.saveTarget();

    expect(service.setTarget).not.toHaveBeenCalled();
  });

  it('keeps the editor open and resolves the message when a save fails', () => {
    service.setTarget.mockReturnValue(httpError('INVALID_PRICE_TARGET'));
    const store = inject();

    store.openTargetEditor();
    store.setTargetDraft('250');
    store.saveTarget();

    expect(store.targetStatus()).toBe('error');
    expect(store.targetErrorMessage()).not.toBe('');
    expect(store.isEditingTarget()).toBe(true);
  });

  it('clears the target', () => {
    service.getDossier.mockReturnValue(of({...DOSSIER, userTarget: TARGET}));
    const store = inject();

    store.clearTarget();

    expect(service.clearTarget).toHaveBeenCalledWith('AAPL');
    expect(store.dossier()?.userTarget).toBeNull();
  });

  it('flags a failed clear and does nothing before the dossier has loaded', () => {
    service.clearTarget.mockReturnValue(httpError('NOPE'));
    const store = inject();

    store.clearTarget();
    expect(store.targetErrorMessage()).toBe('Failed to save your target.');
  });

  it('does not clear before the dossier has loaded', () => {
    service.getDossier.mockReturnValue(NEVER);
    const store = inject();

    store.clearTarget();
    store.saveTarget();

    expect(service.clearTarget).not.toHaveBeenCalled();
    expect(service.setTarget).not.toHaveBeenCalled();
  });
});
