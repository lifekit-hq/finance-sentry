import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {ThesisRevisionService} from './thesis-revision.service';

describe('ThesisRevisionService', () => {
  let service: ThesisRevisionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(ThesisRevisionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the owner queue', () => {
    service.list().subscribe();

    const req = http.expectOne(r => r.url.endsWith('research/thesis-revisions'));
    expect(req.request.method).toBe('GET');
    req.flush({pending: [], decided: []});
  });

  it('confirms with the note and the override flag', () => {
    service.confirm('rev 1', {note: 'ok', overrideRisk: true}).subscribe();

    const req = http.expectOne(r => r.url.endsWith('research/thesis-revisions/rev%201/confirm'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({note: 'ok', overrideRisk: true});
    req.flush({});
  });

  it('rejects with the reason', () => {
    service.reject('rev-1', {reason: 'No.'}).subscribe();

    const req = http.expectOne(r => r.url.endsWith('research/thesis-revisions/rev-1/reject'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({reason: 'No.'});
    req.flush({});
  });
});
