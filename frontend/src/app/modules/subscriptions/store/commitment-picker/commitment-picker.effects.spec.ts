import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {CANDIDATE_SEARCH_DEBOUNCE_MS} from '../../constants/commitment-candidate/commitment-candidate.constants';
import {type CommitmentCandidate} from '../../models/commitment-candidate/commitment-candidate.model';
import {CommitmentCandidatesService} from '../../services/commitment-candidates.service';
import {commitmentPickerEffects} from './commitment-picker.effects';

const CANDIDATE: CommitmentCandidate = {
  transactionId: 'tx-1',
  bankName: 'Test Bank',
  currency: 'EUR',
  amount: 9.99,
  date: '2026-09-01',
  description: 'ACME HOSTING',
  merchantName: 'Acme Hosting',
};

const PAGE = {items: [CANDIDATE], totalCount: 1, offset: 0, limit: 30, hasMore: false};

function buildStore() {
  const search = signal('');
  return {
    search,
    setSearch: vi.fn((value: string) => search.set(value)),
    setLoading: vi.fn(),
    setCandidates: vi.fn(),
    setError: vi.fn(),
  };
}

function setup(result: ReturnType<CommitmentCandidatesService['search']>) {
  const service = {search: vi.fn().mockReturnValue(result)};
  TestBed.configureTestingModule({
    providers: [{provide: CommitmentCandidatesService, useValue: service}],
  });
  const store = buildStore();
  const effects = TestBed.runInInjectionContext(() => commitmentPickerEffects(store));
  return {service, store, effects};
}

describe('commitmentPickerEffects', () => {
  beforeEach(() => TestBed.resetTestingModule());
  afterEach(() => vi.useRealTimers());

  it('load: fetches with the current search and stores the candidates', () => {
    const {service, store, effects} = setup(of(PAGE));

    effects.load();

    expect(store.setLoading).toHaveBeenCalled();
    expect(service.search).toHaveBeenCalledWith('');
    expect(store.setCandidates).toHaveBeenCalledWith([CANDIDATE]);
  });

  it('load: keeps the error code when the fetch fails', () => {
    const {store, effects} = setup(
      throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'INVALID_SEARCH'}}))
    );

    effects.load();

    expect(store.setError).toHaveBeenCalledWith('INVALID_SEARCH');
  });

  it('applySearch: debounces, then reloads with the new term', () => {
    vi.useFakeTimers();
    const {service, store, effects} = setup(of(PAGE));

    effects.applySearch('acme');
    expect(service.search).not.toHaveBeenCalled();

    vi.advanceTimersByTime(CANDIDATE_SEARCH_DEBOUNCE_MS);

    expect(store.setSearch).toHaveBeenCalledWith('acme');
    expect(service.search).toHaveBeenCalledWith('acme');
  });

  it('applySearch: ignores a term equal to the current one', () => {
    vi.useFakeTimers();
    const {service, effects} = setup(of(PAGE));

    effects.applySearch('  ');
    vi.advanceTimersByTime(CANDIDATE_SEARCH_DEBOUNCE_MS);

    expect(service.search).not.toHaveBeenCalled();
  });
});
