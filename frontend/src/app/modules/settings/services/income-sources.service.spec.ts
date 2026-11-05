import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type IncomeSourceInput} from '../models/income-source/income-source.model';
import {IncomeSourcesService} from './income-sources.service';

const INPUT: IncomeSourceInput = {
  kind: 'Salary',
  label: 'Day job',
  amount: 1000,
  currency: 'EUR',
  cadence: 'Monthly',
  grossOrNet: 'Net',
  startDate: '2026-01-01',
  endDate: null,
};

describe('IncomeSourcesService', () => {
  let service: IncomeSourcesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(IncomeSourcesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('lists the declared sources', () => {
    service.list().subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/income-sources'));
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('creates a source from the input', () => {
    service.create(INPUT).subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/income-sources'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(INPUT);
    req.flush({});
  });

  it('updates a source by id', () => {
    service.update('s-1', INPUT).subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/income-sources/s-1'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(INPUT);
    req.flush({});
  });

  it('retires a source without a body', () => {
    service.retire('s-1').subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/income-sources/s-1/retire'));
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('deletes a source by id', () => {
    service.remove('s-1').subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/income-sources/s-1'));
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
