import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type QuoteDto} from '../models/quote/quote.model';
import {QuotesService} from './quotes.service';

const BASE = 'http://api.test';

describe('QuotesService', () => {
  let service: QuotesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(QuotesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('fetches every ticker in one comma-joined call', () => {
    const body: QuoteDto[] = [
      {
        ticker: 'AAPL',
        resolvedTicker: null,
        price: 200,
        previousClose: 198,
        changePct: 1.01,
        currency: 'USD',
      },
    ];
    let result: QuoteDto[] = [];
    service.getQuotes(['AAPL', 'MSFT']).subscribe(r => (result = r));

    const req = http.expectOne(r => r.url === `${BASE}/research/quotes`);
    expect(req.request.params.get('tickers')).toBe('AAPL,MSFT');
    req.flush(body);

    expect(result).toEqual(body);
  });
});
