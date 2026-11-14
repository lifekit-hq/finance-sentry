import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {GapModeFixtures} from '../../models/gap-mode/gap-mode.fixtures';
import {UniverseScreenFixtures} from '../../models/universe-screen/universe-screen.fixtures';
import {PortfolioConstructionService} from '../../services/portfolio-construction.service';
import {UniverseScreenStore} from './universe-screen.store';

function setup(service: Partial<Record<keyof PortfolioConstructionService, unknown>>) {
  TestBed.configureTestingModule({
    providers: [
      UniverseScreenStore,
      {
        provide: PortfolioConstructionService,
        useValue: {getGap: vi.fn().mockReturnValue(of(GapModeFixtures.gap())), ...service},
      },
      {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
    ],
  });
  return TestBed.inject(UniverseScreenStore);
}

describe('UniverseScreenStore', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('loads the configured default archetype on init', () => {
    const getUniverse = vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen()));

    const store = setup({getUniverse});

    expect(getUniverse).toHaveBeenCalledWith(undefined);
    expect(store.screen()?.archetype).toBe('growth');
    expect(store.isLoadLoading()).toBe(false);
  });

  it('offers the archetypes as segments and names the one the rows were built for', () => {
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen())),
    });

    expect(store.archetypeSegments()).toEqual([
      {value: 'balanced', label: 'Balanced'},
      {value: 'growth', label: 'Growth'},
    ]);
    expect(store.activeArchetype()?.label).toBe('Growth');
  });

  it('asks for the chosen archetype when it reloads', () => {
    const getUniverse = vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen()));
    const store = setup({getUniverse});

    store.setArchetype('balanced');
    store.load();

    expect(getUniverse).toHaveBeenLastCalledWith('balanced');
  });

  it('orders the suggested book first and a name that failed to score last', () => {
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen())),
    });

    expect(store.rows().map(r => r.row.symbol)).toEqual(['AAA', 'BBB', 'CCC']);
  });

  it('filters by verdict word and re-sorts by one raw axis', () => {
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen())),
    });

    store.setVerdictFilter('Wait');
    expect(store.rows().map(r => r.row.symbol)).toEqual(['BBB']);

    store.setVerdictFilter('All');
    store.setSortKey('symbol');
    expect(store.rows().map(r => r.row.symbol)).toEqual(['AAA', 'BBB', 'CCC']);
  });

  it('reports an empty universe', () => {
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen({rows: []}))),
    });

    expect(store.hasRows()).toBe(false);
  });

  it('explains an unknown archetype from the error registry', () => {
    const error = new HttpErrorResponse({status: 400, error: {errorCode: 'UNKNOWN_ARCHETYPE'}});
    const store = setup({getUniverse: vi.fn().mockReturnValue(throwError(() => error))});

    expect(store.loadErrorMessage()).toBe('That archetype does not exist. Pick one from the list.');
    expect(store.screen()).toBeNull();
  });

  it('falls back to the flow message for an unregistered error', () => {
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({status: 500}))),
    });

    expect(store.loadErrorMessage()).toBe('Failed to load the universe.');
  });

  it('loads how the book compares for the archetype the screen built', () => {
    const getGap = vi.fn().mockReturnValue(of(GapModeFixtures.gap()));
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen())),
      getGap,
    });

    expect(getGap).toHaveBeenCalledWith('growth');
    expect(store.gap()?.summary.missingNames).toEqual(['BBB']);
    expect(store.isGapLoading()).toBe(false);
  });

  it('reloads the gap when the archetype changes', () => {
    const getGap = vi.fn().mockReturnValue(of(GapModeFixtures.gap()));
    const store = setup({
      getUniverse: vi
        .fn()
        .mockReturnValueOnce(of(UniverseScreenFixtures.screen()))
        .mockReturnValueOnce(of(UniverseScreenFixtures.screen({archetype: 'balanced'}))),
      getGap,
    });

    store.setArchetype('balanced');
    store.load();

    expect(getGap).toHaveBeenLastCalledWith('balanced');
  });

  it('keeps the screen when the gap cannot be loaded, and can retry it', () => {
    const getGap = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({status: 500})))
      .mockReturnValueOnce(of(GapModeFixtures.gap()));
    const store = setup({
      getUniverse: vi.fn().mockReturnValue(of(UniverseScreenFixtures.screen())),
      getGap,
    });

    expect(store.screen()).not.toBeNull();
    expect(store.gap()).toBeNull();
    expect(store.gapErrorMessage()).toBe('Failed to load how your book compares.');

    store.loadGap();

    expect(store.gap()).not.toBeNull();
    expect(store.gapErrorMessage()).toBe('');
  });
});
