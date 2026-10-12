import {TestBed} from '@angular/core/testing';
import {SEED_STORAGE_KEY} from '@lifekit-hq/tokens/engine';
import {LOCAL_STORAGE} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it} from 'vitest';

import {AppearanceStore} from './appearance.store';

const SEED = '#4f46e5';
const INTENSITY = 0.3;

// A reload keeps localStorage and drops everything else, so each "boot" gets a fresh injector
// over the same storage.
function boot(storage: Storage) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [AppearanceStore, {provide: LOCAL_STORAGE, useValue: storage}],
  });
  return TestBed.inject(AppearanceStore);
}

describe('AppearanceStore', () => {
  let storage: Storage;

  beforeEach(() => {
    const backing = new Map<string, string>();
    storage = {
      getItem: key => backing.get(key) ?? null,
      setItem: (key, value) => void backing.set(key, value),
      removeItem: key => void backing.delete(key),
      clear: () => backing.clear(),
      key: index => [...backing.keys()][index] ?? null,
      get length() {
        return backing.size;
      },
    };
  });

  it('starts on the app palette', () => {
    expect(boot(storage).seed()).toBeNull();
  });

  it('applies a picked colour and its intensity', () => {
    const store = boot(storage);
    store.choose({seed: SEED, intensity: INTENSITY});
    expect(store.seed()).toBe(SEED);
    expect(store.intensity()).toBe(INTENSITY);
  });

  it('persists the pick across a reload', () => {
    boot(storage).choose({seed: SEED, intensity: INTENSITY});
    expect(storage.getItem(SEED_STORAGE_KEY)).not.toBeNull();

    const reloaded = boot(storage);
    expect(reloaded.seed()).toBe(SEED);
    expect(reloaded.intensity()).toBe(INTENSITY);
  });

  it('returns to the app palette on reset and forgets the stored colour', () => {
    const store = boot(storage);
    store.choose({seed: SEED, intensity: INTENSITY});
    store.choose({seed: null, intensity: INTENSITY});
    expect(store.seed()).toBeNull();
    expect(store.intensity()).toBe(INTENSITY);
    expect(storage.getItem(SEED_STORAGE_KEY)).toBeNull();
    expect(boot(storage).seed()).toBeNull();
  });
});
