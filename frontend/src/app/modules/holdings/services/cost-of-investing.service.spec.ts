import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type SleeveCosts} from '../models/cost-of-investing/cost-of-investing.model';
import {CostOfInvestingService} from './cost-of-investing.service';

const BASE = 'http://api.test';

describe('CostOfInvestingService', () => {
  let service: CostOfInvestingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(CostOfInvestingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the brokerage costs from the wealth endpoint', () => {
    let result: unknown = null;
    service.getCosts('brokerage').subscribe(costs => (result = costs));

    const body: Partial<SleeveCosts> = {total: 85, commissions: 30};
    http.expectOne(`${BASE}/wealth/brokerage/costs`).flush(body);

    expect(result).toEqual(body);
  });

  it('reads the crypto costs from its own sleeve endpoint', () => {
    service.getCosts('crypto').subscribe();

    http.expectOne(`${BASE}/wealth/crypto/costs`).flush({});
  });
});
