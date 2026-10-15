import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
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
    providers: [
      WatchlistStore,
      {provide: WatchlistService, useValue: service},
      {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
    ],
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
    const onAdded = vi.fn();
    const reply = new Subject<WatchlistItem>();
    const service = {
      getItems: vi.fn().mockReturnValue(of([PLTR])),
      add: vi.fn().mockReturnValue(reply),
    };
    const store = setup(service);

    store.add({payload: {ticker: 'MRNA', exchange: 'NASDAQ', note: null}, onAdded});

    expect(store.rows().map(r => r.ticker)).toContain('MRNA');
    expect(onAdded).not.toHaveBeenCalled();
    reply.next(MRNA);
    reply.complete();
    expect(onAdded).toHaveBeenCalledTimes(1);
    expect(store.items().map(i => i.id)).toEqual(['w1', 'w2']);
  });

  it('takes an added row back out when the add fails', () => {
    const onAdded = vi.fn();
    const store = setup({
      getItems: vi.fn().mockReturnValue(of([PLTR])),
      add: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({status: 400}))),
    });

    store.add({payload: {ticker: 'MRNA', exchange: null, note: null}, onAdded});

    expect(store.items()).toEqual([PLTR]);
    expect(store.addErrorMessage()).not.toBe('');
    expect(onAdded).not.toHaveBeenCalled();
  });

  it('leaves a row removed by another call gone when a later delete fails', () => {
    const first = new Subject<void>();
    const second = new Subject<void>();
    const store = setup({
      getItems: vi.fn().mockReturnValue(of([PLTR, MRNA])),
      remove: vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(second),
    });

    store.remove('w1');
    store.remove('w2');
    second.next();
    second.complete();
    first.error(new HttpErrorResponse({status: 500}));

    expect(store.items()).toEqual([PLTR]);
  });

  it('ignores an add and a remove while the load is in flight', () => {
    const service = {
      getItems: vi.fn().mockReturnValue(new Subject<WatchlistItem[]>()),
      add: vi.fn(),
      remove: vi.fn(),
    };
    const store = setup(service);

    store.add({payload: {ticker: 'MRNA', exchange: null, note: null}, onAdded: vi.fn()});
    store.remove('w1');

    expect(service.add).not.toHaveBeenCalled();
    expect(service.remove).not.toHaveBeenCalled();
    expect(store.items()).toEqual([]);
  });

  it('keeps a pending add when a retried load answers before it', () => {
    const load = new Subject<WatchlistItem[]>();
    const reply = new Subject<WatchlistItem>();
    const store = setup({
      getItems: vi
        .fn()
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({status: 500})))
        .mockReturnValue(load),
      add: vi.fn().mockReturnValue(reply),
    });

    store.add({payload: {ticker: 'MRNA', exchange: null, note: null}, onAdded: vi.fn()});
    store.load();
    load.next([PLTR]);
    load.complete();
    reply.next(MRNA);
    reply.complete();

    expect(store.items().map(i => i.id)).toEqual(['w1', 'w2']);
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

  it('keeps a row gone when the server no longer has it', () => {
    const store = setup({
      getItems: vi.fn().mockReturnValue(of([PLTR, MRNA])),
      remove: vi
        .fn()
        .mockReturnValue(
          throwError(
            () =>
              new HttpErrorResponse({status: 404, error: {errorCode: 'WATCHLIST_ITEM_NOT_FOUND'}})
          )
        ),
    });

    store.remove('w1');

    expect(store.items()).toEqual([MRNA]);
    expect(store.removeErrorMessage()).toBe('That watchlist item no longer exists.');
  });

  it('shows only the error of the latest action', () => {
    const store = setup({
      getItems: vi.fn().mockReturnValue(of([PLTR])),
      remove: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({status: 500}))),
      add: vi.fn().mockReturnValue(of(MRNA)),
    });

    store.remove('w1');
    expect(store.actionErrorMessage()).toBe('Failed to remove from your watchlist.');

    store.add({payload: {ticker: 'MRNA', exchange: null, note: null}, onAdded: vi.fn()});
    expect(store.actionErrorMessage()).toBe('');
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
