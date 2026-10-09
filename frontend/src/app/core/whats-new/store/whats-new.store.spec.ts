import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../modules/auth/store/auth.store';
import {OWNER_ROLE} from '../../../shared/constants/role/role.constants';
import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {WHATS_NEW_SEEN_STORAGE_KEY} from '../constants/whats-new.constants';
import {type WhatsNewData, type WhatsNewNote} from '../models/whats-new.model';
import {WhatsNewService} from '../services/whats-new.service';
import {WhatsNewStore} from './whats-new.store';

const OLDER = '0.0.1';
const MEMBER_NOTE: WhatsNewNote = {text: 'You can do a thing.', owner: false};
const OWNER_NOTE: WhatsNewNote = {text: 'You can invite people.', owner: true};

const data = (notes: WhatsNewNote[]): WhatsNewData => ({
  versions: [
    {version: APP_VERSION, date: '2026-10-05', notes, changes: ['Show a thing']},
    {version: OLDER, date: '2026-01-01', notes: [], changes: []},
  ],
});

describe('WhatsNewStore', () => {
  const load = vi.fn();

  const create = (options: {roles?: string[]; payload?: WhatsNewData; fails?: boolean} = {}) => {
    load.mockReset();
    load.mockReturnValue(
      options.fails
        ? throwError(() => new Error('offline'))
        : of(options.payload ?? data([MEMBER_NOTE]))
    );
    TestBed.configureTestingModule({
      providers: [
        {provide: WhatsNewService, useValue: {load}},
        {provide: AuthStore, useValue: {roles: signal(options.roles ?? [])}},
      ],
    });
    return TestBed.inject(WhatsNewStore);
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  describe('the unread dot', () => {
    it('records the running version on a first run, with no dot and no fetch', () => {
      const store = create();

      expect(store.hasUnread()).toBe(false);
      expect(store.lastSeen()).toBe(APP_VERSION);
      expect(localStorage.getItem(WHATS_NEW_SEEN_STORAGE_KEY)).toBe(APP_VERSION);
      expect(load).not.toHaveBeenCalled();
    });

    it('shows when the running version is newer than the last seen and has notes', () => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, OLDER);

      const store = create();

      expect(load).toHaveBeenCalledOnce();
      expect(store.hasUnread()).toBe(true);
    });

    it('stays off when the newer version has no plain notes', () => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, OLDER);

      const store = create({payload: data([])});

      expect(store.hasUnread()).toBe(false);
    });

    it('stays off when this version was already seen, without fetching', () => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, APP_VERSION);

      const store = create();

      expect(store.hasUnread()).toBe(false);
      expect(load).not.toHaveBeenCalled();
    });

    it('is cleared by opening the panel and the version is kept on this device', () => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, OLDER);
      const store = create();

      store.markSeen();

      expect(store.hasUnread()).toBe(false);
      expect(localStorage.getItem(WHATS_NEW_SEEN_STORAGE_KEY)).toBe(APP_VERSION);
    });

    it('starts again from what this device stored, not from another device', () => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, OLDER);
      expect(create().hasUnread()).toBe(true);

      TestBed.resetTestingModule();
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, APP_VERSION);

      expect(create().hasUnread()).toBe(false);
    });

    it('stays off when the failed fetch leaves nothing to say it has notes', () => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, OLDER);

      const store = create({fails: true});

      expect(store.hasUnread()).toBe(false);
      expect(store.viewStatus()).toBe('error');
    });
  });

  describe('owner lines', () => {
    beforeEach(() => {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, OLDER);
    });

    it('hides them from a Member', () => {
      const store = create({payload: data([MEMBER_NOTE, OWNER_NOTE])});

      expect(store.visibleVersions()[0].notes).toEqual([MEMBER_NOTE]);
    });

    it('shows them to the Owner', () => {
      const store = create({roles: [OWNER_ROLE], payload: data([MEMBER_NOTE, OWNER_NOTE])});

      expect(store.visibleVersions()[0].notes).toEqual([MEMBER_NOTE, OWNER_NOTE]);
    });

    it('gives a Member no dot when the release only has owner lines', () => {
      expect(create({payload: data([OWNER_NOTE])}).hasUnread()).toBe(false);
    });

    it('gives the Owner the dot for the same release', () => {
      expect(create({roles: [OWNER_ROLE], payload: data([OWNER_NOTE])}).hasUnread()).toBe(true);
    });
  });

  describe('loading', () => {
    it('fetches once and shows the panel content', () => {
      const store = create();

      store.load();
      store.load();

      expect(load).toHaveBeenCalledOnce();
      expect(store.viewStatus()).toBe('success');
      expect(store.visibleVersions()).toHaveLength(2);
    });

    it('reports an error and fetches again on retry', () => {
      const store = create({fails: true});

      store.load();
      expect(store.viewStatus()).toBe('error');
      expect(store.errorMessage()).not.toBe('');

      load.mockReturnValue(of(data([MEMBER_NOTE])));
      store.load();

      expect(store.viewStatus()).toBe('success');
      expect(store.errorMessage()).toBe('');
    });
  });
});
