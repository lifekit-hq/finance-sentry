import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {WatchlistService} from './watchlist.service';

describe('WatchlistService', () => {
  let service: WatchlistService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(WatchlistService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('getItems reads the watchlist', () => {
    let result: unknown;
    service.getItems().subscribe(items => (result = items));

    const req = http.expectOne(r => r.url.endsWith('research/watchlist'));
    expect(req.request.method).toBe('GET');
    const items = [
      {id: 'w1', ticker: 'PLTR', exchange: null, note: 'buy the dip', addedAt: '2026-10-14'},
    ];
    req.flush(items);
    expect(result).toEqual(items);
  });

  it('add posts ticker, exchange and note', () => {
    service.add({ticker: 'MRNA', exchange: 'NASDAQ', note: null}).subscribe();

    const req = http.expectOne(r => r.url.endsWith('research/watchlist'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ticker: 'MRNA', exchange: 'NASDAQ', note: null});
    req.flush({id: 'w2', ticker: 'MRNA', exchange: 'NASDAQ', note: null, addedAt: '2026-10-15'});
  });

  it('remove deletes the item by id', () => {
    service.remove('w1').subscribe();

    const req = http.expectOne(r => r.url.endsWith('research/watchlist/w1'));
    expect(req.request.method).toBe('DELETE');
    req.flush(null, {status: 204, statusText: 'No Content'});
  });
});
