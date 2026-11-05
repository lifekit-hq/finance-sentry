import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {UniverseScreenFixtures} from '../models/universe-screen/universe-screen.fixtures';
import {PortfolioConstructionService} from './portfolio-construction.service';

describe('PortfolioConstructionService', () => {
  let service: PortfolioConstructionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(PortfolioConstructionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('asks for the configured default archetype when none is named', () => {
    let result: unknown;
    service.getUniverse().subscribe(screen => (result = screen));

    const req = http.expectOne(r => r.url.endsWith('portfolio-construction/universe'));
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('archetype')).toBe(false);
    const screen = UniverseScreenFixtures.screen();
    req.flush(screen);
    expect(result).toEqual(screen);
  });

  it('names the archetype in the query', () => {
    service.getUniverse('balanced').subscribe();

    const req = http.expectOne(r => r.url.endsWith('portfolio-construction/universe'));
    expect(req.request.params.get('archetype')).toBe('balanced');
    req.flush(UniverseScreenFixtures.screen());
  });
});
