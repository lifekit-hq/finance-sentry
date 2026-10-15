import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type BrokerageGrowth} from '../models/brokerage-growth/brokerage-growth.model';
import {BrokerageGrowthService} from './brokerage-growth.service';

const BASE = 'http://api.test';

describe('BrokerageGrowthService', () => {
  let service: BrokerageGrowthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(BrokerageGrowthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the brokerage split from the wealth endpoint', () => {
    let result: unknown = null;
    service.getGrowth().subscribe(growth => (result = growth));

    const body: Partial<BrokerageGrowth> = {seriesStart: '2026-07-01', growth: 110};
    http.expectOne(`${BASE}/wealth/brokerage/growth`).flush(body);

    expect(result).toEqual(body);
  });
});
