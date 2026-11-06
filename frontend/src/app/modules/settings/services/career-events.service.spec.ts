import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type CareerEventInput} from '../models/career-event/career-event.model';
import {CareerEventsService} from './career-events.service';

const INPUT: CareerEventInput = {
  kind: 'IncomeStep',
  date: '2027-03-01',
  endDate: null,
  label: 'Promotion',
  amount: 500,
  currency: 'EUR',
  note: null,
};

describe('CareerEventsService', () => {
  let service: CareerEventsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(CareerEventsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('lists the career events', () => {
    service.list().subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/career-events'));
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('creates an event from the input', () => {
    service.create(INPUT).subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/career-events'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(INPUT);
    req.flush({});
  });

  it('updates an event by id', () => {
    service.update('e-1', INPUT).subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/career-events/e-1'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(INPUT);
    req.flush({});
  });

  it('retires an event without a body', () => {
    service.retire('e-1').subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/career-events/e-1/retire'));
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('deletes an event by id', () => {
    service.remove('e-1').subscribe();

    const req = http.expectOne(r => r.url.endsWith('wealth/career-events/e-1'));
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
