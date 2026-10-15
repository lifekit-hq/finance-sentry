import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type WatchlistItem} from '../../models/watchlist/watchlist.model';
import {WatchlistService} from '../../services/watchlist.service';
import {WatchlistStore} from './watchlist.store';

const PLTR: WatchlistItem = {
  id: 'w1',
  ticker: 'PLTR',
  exchange: null,
  note: 'Add on pullback to the 50d',
  addedAt: '2026-10-14T09:00:00Z',
};
const MRNA: WatchlistItem = {
  id: 'w2',
  ticker: 'MRNA',
  exchange: 'NASDAQ',
  note: null,
  addedAt: '2026-10-10T09:00:00Z',
};

function setup(service: Partial<Record<keyof WatchlistService, unknown>>) {
  TestBed.configureTestingModule({
    providers: [WatchlistStore, {provide: WatchlistService, useValue: service}],
  });
  return TestBed.inject(WatchlistStore);
}

describe('WatchlistStore', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads the list on init, newest first, each row linking to its dossier', () => {
    const store = setup({getItems: vi.fn().mockReturnValue(of([MRNA, PLTR]))});

    expect(store.rows().map(r => r.ticker)).toEqual(['PLTR', 'MRNA']);
    expect(store.rows()[0].dossierRoute).toBe('/assets/PLTR');
    expect(store.isLoadLoading()).toBe(false);
  });

  it('keeps an error from the load and an empty list', () => {
    const store = setup({
      getItems: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({status: 500}))),
    });

    expect(store.loadErrorMessage()).not.toBe('');
    expect(store.rows()).toEqual([]);
  });

  it('shows an added row before the server answers, then swaps in the saved one', () => {
    const reply = new Subject<WatchlistItem>();
    const service = {
      getItems: vi.fn().mockReturnValue(of([PLTR])),
      add: vi.fn().mockReturnValue(reply),
    };
    const store = setup(service);

    store.add({ticker: 'MRNA', exchange: 'NASDAQ', note: null});

    expect(store.rows().map(r => r.ticker)).toContain('MRNA');
    reply.next(MRNA);
    reply.complete();
    expect(store.items().map(i => i.id)).toEqual(['w1', 'w2']);
  });

  it('takes an added row back out when the add fails', () => {
    const store = setup({
      getItems: vi.fn().mockReturnValue(of([PLTR])),
      add: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({status: 400}))),
    });

    store.add({ticker: 'MRNA', exchange: null, note: null});

    expect(store.items()).toEqual([PLTR]);
    expect(store.addErrorMessage()).not.toBe('');
  });

  it('removes a row at once and deletes it on the server', () => {
    const service = {
      getItems: vi.fn().mockReturnValue(of([PLTR, MRNA])),
      remove: vi.fn().mockReturnValue(of(undefined)),
    };
    const store = setup(service);

    store.remove('w1');

    expect(service.remove).toHaveBeenCalledWith('w1');
    expect(store.items()).toEqual([MRNA]);
  });

  it('puts a row back when its delete fails', () => {
    const store = setup({
      getItems: vi.fn().mockReturnValue(of([PLTR, MRNA])),
      remove: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({status: 500}))),
    });

    store.remove('w1');

    expect(store.items()).toEqual([PLTR, MRNA]);
    expect(store.removeErrorMessage()).not.toBe('');
  });
});
