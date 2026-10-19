import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type BookPerformance} from '../models/book-performance/book-performance.model';
import {BookPerformanceService} from './book-performance.service';

const BASE = 'http://api.test';

describe('BookPerformanceService', () => {
  let service: BookPerformanceService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(BookPerformanceService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the book against SPY from the radar endpoint', () => {
    let result: unknown = null;
    service.getPerformance().subscribe(performance => (result = performance));

    const body: BookPerformance = {periods: [], asOf: '2026-10-19'};
    http.expectOne(`${BASE}/radar/book-performance`).flush(body);

    expect(result).toEqual(body);
  });
});
