import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {BankSyncService} from './bank-sync.service';

const POLL_INTERVAL_MS = 2000;
const SYNC_STATUS_URL = 'http://api.test/accounts/a1/sync-status';

describe('BankSyncService.pollSyncStatus', () => {
  let service: BankSyncService;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(BankSyncService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('stops polling once the last subscriber unsubscribes', () => {
    const subscription = service.pollSyncStatus('a1', POLL_INTERVAL_MS).subscribe();
    vi.advanceTimersByTime(0);
    http.expectOne(SYNC_STATUS_URL).flush({status: 'running'});

    subscription.unsubscribe();
    vi.advanceTimersByTime(POLL_INTERVAL_MS * 3);

    http.expectNone(SYNC_STATUS_URL);
  });

  it('completes after a terminal status', () => {
    const statuses: string[] = [];
    let completed = false;
    service.pollSyncStatus('a1', POLL_INTERVAL_MS).subscribe({
      next: s => statuses.push(s.status),
      complete: () => (completed = true),
    });
    vi.advanceTimersByTime(0);

    http.expectOne(SYNC_STATUS_URL).flush({status: 'running'});
    vi.advanceTimersByTime(POLL_INTERVAL_MS);
    http.expectOne(SYNC_STATUS_URL).flush({status: 'success'});

    expect(statuses).toEqual(['running', 'success']);
    expect(completed).toBe(true);
  });
});
