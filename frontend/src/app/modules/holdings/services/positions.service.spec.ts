import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type CryptoHoldingsDto, type Position} from '../models/position/position.model';
import {PositionsService} from './positions.service';

const BASE = 'http://api.test';

describe('PositionsService', () => {
  let service: PositionsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(PositionsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('labels each crypto position with the venue it is held on', () => {
    const crypto: CryptoHoldingsDto = {
      provider: 'multiple',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 36_000,
      holdings: [
        {asset: 'BTC', freeQuantity: 0.5, lockedQuantity: 0, usdValue: 30_000, provider: 'binance'},
        {
          asset: 'BTC',
          freeQuantity: 0.1,
          lockedQuantity: 0,
          usdValue: 6_000,
          provider: 'revolut_x',
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(null);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(crypto);

    expect(positions.map(p => [p.symbol, p.provider, p.currentValue])).toEqual([
      ['BTC', 'binance', 30_000],
      ['BTC', 'revolut_x', 6_000],
    ]);
    expect(positions.every(p => p.pnlPercent === null)).toBe(true);
  });
});
