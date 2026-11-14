import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type ScorecardVerdictSummary} from '../../../shared/models/scorecard/scorecard.model';
import {VerdictsService} from './verdicts.service';

const BASE = 'http://api.test';

describe('VerdictsService', () => {
  let service: VerdictsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(VerdictsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads every symbol in one comma-joined call', () => {
    const body: ScorecardVerdictSummary[] = [
      {symbol: 'AAA', word: 'Buy', firedRuleId: 'buy', turnsBuyWhen: null},
    ];
    let result: ScorecardVerdictSummary[] = [];

    service.getVerdicts(['AAA', 'BBB']).subscribe(r => (result = r));

    const req = http.expectOne(r => r.url === `${BASE}/research/scorecards/verdicts`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('symbols')).toBe('AAA,BBB');
    req.flush(body);
    expect(result).toEqual(body);
  });
});
