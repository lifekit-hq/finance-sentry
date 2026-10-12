import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {AuthStore} from '../../auth/store/auth.store';
import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
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

function httpError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 500, error: {errorCode}}));
}

describe('DossierStore', () => {
  let service: {
    getDossier: ReturnType<typeof vi.fn>;
    getQuotes: ReturnType<typeof vi.fn>;
    getLedgerRead: ReturnType<typeof vi.fn>;
    generateLedgerRead: ReturnType<typeof vi.fn>;
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
});
