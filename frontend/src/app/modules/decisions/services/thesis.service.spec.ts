import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {ThesisService} from './thesis.service';

describe('ThesisService', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it("reads the owner's theses", () => {
    TestBed.inject(ThesisService).list().subscribe();

    const req = http.expectOne(r => r.url.endsWith('research/theses'));
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
