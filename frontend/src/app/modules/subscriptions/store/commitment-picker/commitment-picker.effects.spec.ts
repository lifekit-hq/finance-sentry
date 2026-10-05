import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {CANDIDATE_SEARCH_DEBOUNCE_MS} from '../../constants/commitment-candidate/commitment-candidate.constants';
import {
  type CommitmentAnchor,
  type CommitmentCandidate,
} from '../../models/commitment-candidate/commitment-candidate.model';
import {CommitmentCandidatesService} from '../../services/commitment-candidates.service';
import {SubscriptionsService} from '../../services/subscriptions.service';
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
    setAnchor: vi.fn(),
  };
}

const ANCHOR: CommitmentAnchor = {
  amount: 12.99,
  currency: 'EUR',
  date: '2026-10-05',
  chargeCount: 3,
  cadence: 'monthly',
};

function setup(
  result: ReturnType<CommitmentCandidatesService['search']>,
  anchor: ReturnType<SubscriptionsService['getCommitmentAnchor']> = of(ANCHOR)
) {
  const service = {search: vi.fn().mockReturnValue(result)};
  const subscriptions = {getCommitmentAnchor: vi.fn().mockReturnValue(anchor)};
  TestBed.configureTestingModule({
    providers: [
      {provide: CommitmentCandidatesService, useValue: service},
      {provide: SubscriptionsService, useValue: subscriptions},
    ],
  });
  const store = buildStore();
  const effects = TestBed.runInInjectionContext(() => commitmentPickerEffects(store));
  return {service, subscriptions, store, effects};
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

  it('loadAnchor: stores the latest charge and hands it to the caller', () => {
    const {subscriptions, store, effects} = setup(of(PAGE));
    const onLoaded = vi.fn();
    effects.loadAnchor({transactionId: 'tx-1', onLoaded});
    expect(subscriptions.getCommitmentAnchor).toHaveBeenCalledWith('tx-1');
    expect(store.setAnchor).toHaveBeenCalledWith(ANCHOR);
    expect(onLoaded).toHaveBeenCalledWith(ANCHOR);
  });

  it('loadAnchor: leaves the pick as it is when the lookup fails', () => {
    const {store, effects} = setup(
      of(PAGE),
      throwError(() => new HttpErrorResponse({status: 404}))
    );
    const onLoaded = vi.fn();
    effects.loadAnchor({transactionId: 'tx-1', onLoaded});
    expect(store.setAnchor).not.toHaveBeenCalled();
    expect(onLoaded).not.toHaveBeenCalled();
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
