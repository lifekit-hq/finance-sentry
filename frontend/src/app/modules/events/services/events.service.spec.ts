import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {EventsService} from './events.service';

describe('EventsService', () => {
  let service: EventsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(EventsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('getUpcoming sends the window and omits kinds when every kind is wanted', () => {
    service.getUpcoming('2026-09-22', '2026-10-22', []).subscribe();

    const req = http.expectOne(r => r.url.endsWith('events/upcoming'));
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-09-22');
    expect(req.request.params.get('to')).toBe('2026-10-22');
    expect(req.request.params.has('kinds')).toBe(false);
    req.flush({items: [], from: '2026-09-22', to: '2026-10-22', sources: []});
  });

  it('getUpcoming joins selected kinds with commas', () => {
    service.getUpcoming('2026-09-22', '2026-10-22', ['macro', 'earnings']).subscribe();

    const req = http.expectOne(r => r.url.endsWith('events/upcoming'));
    expect(req.request.params.get('kinds')).toBe('macro,earnings');
    req.flush({items: [], from: '2026-09-22', to: '2026-10-22', sources: []});
  });

  it('getFired sends page and page size', () => {
    service.getFired(2, 20).subscribe();

    const req = http.expectOne(r => r.url.endsWith('events/fired'));
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({items: [], totalCount: 0, page: 2, pageSize: 20, totalPages: 0});
  });
});
