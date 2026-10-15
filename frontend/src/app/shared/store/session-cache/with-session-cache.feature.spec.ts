import {Injector, runInInjectionContext} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {signalStore, withState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {SessionCacheStore} from './session-cache.store';
import {withSessionCache} from './with-session-cache.feature';

interface PageState {
  rows: string[];
  filter: string;
  restored: boolean;
  scratch: number;
}

const INITIAL: PageState = {rows: [], filter: 'all', restored: false, scratch: 0};

const PageStore = signalStore(
  withState(INITIAL),
  withSessionCache<PageState>(
    'page',
    ['rows', 'filter'],
    (kept, current) => kept.filter === current.filter,
    {restored: true}
  )
);

function open(): {store: InstanceType<typeof PageStore>; close: () => void} {
  const injector = Injector.create({
    providers: [PageStore],
    parent: TestBed.inject(Injector),
  }) as Injector & {destroy: () => void};
  const store = runInInjectionContext(injector, () => injector.get(PageStore));
  return {store, close: () => injector.destroy()};
}

describe('withSessionCache', () => {
  it('starts empty on a first visit', () => {
    const {store} = open();
    expect(store.rows()).toEqual([]);
    expect(store.restored()).toBe(false);
  });

  it('repaints the last visit and flags it as restored, without the fields it does not keep', () => {
    const cache = TestBed.inject(SessionCacheStore);
    cache.write('page', {rows: ['a', 'b'], filter: 'all'});
    const second = open();

    expect(second.store.rows()).toEqual(['a', 'b']);
    expect(second.store.restored()).toBe(true);
    expect(second.store.scratch()).toBe(0);
  });

  it('refuses a snapshot that does not fit the current state', () => {
    TestBed.inject(SessionCacheStore).write('page', {rows: ['a'], filter: 'unread'});
    const {store} = open();

    expect(store.rows()).toEqual([]);
    expect(store.restored()).toBe(false);
  });

  it('keeps only the named fields when the page closes, and clear() forgets them', () => {
    const cache = TestBed.inject(SessionCacheStore);
    cache.write('page', {rows: ['a'], filter: 'all'});
    const {close} = open();
    close();

    expect(cache.read('page')).toEqual({rows: ['a'], filter: 'all'});
    cache.clear();
    expect(cache.read('page')).toBeUndefined();
  });
});
