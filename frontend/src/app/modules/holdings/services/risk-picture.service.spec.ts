import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type RiskPicture} from '../models/risk-picture/risk-picture.model';
import {RiskPictureService} from './risk-picture.service';

const BASE = 'http://api.test';

describe('RiskPictureService', () => {
  let service: RiskPictureService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(RiskPictureService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the risk picture from the radar endpoint', () => {
    let result: unknown = null;
    service.getRiskPicture().subscribe(picture => (result = picture));

    const body: RiskPicture = {
      from: '2026-01-31',
      to: '2026-09-30',
      monthsObserved: 8,
      betaMonths: 7,
      volatility: {value: 0.18, reason: null},
      maxDrawdown: {value: 0.2, reason: null},
      beta: {value: null, reason: 'NotEnoughHistory'},
      drawdownTolerance: null,
      top1Symbol: 'DRAM',
      top1Weight: 0.378,
      top3Symbols: ['DRAM'],
      top3Weight: 0.378,
      cryptoShare: 0.1,
      positionFlag: 0.3,
      top1OverFlag: true,
    };
    http.expectOne(`${BASE}/radar/risk-picture`).flush(body);

    expect(result).toEqual(body);
  });
});
