import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {UniverseScreenFixtures} from '../../models/universe-screen/universe-screen.fixtures';
import {PortfolioConstructionService} from '../../services/portfolio-construction.service';
import {UniverseScreenStore} from './universe-screen.store';

function setup(service: Partial<Record<keyof PortfolioConstructionService, unknown>>) {
  TestBed.configureTestingModule({
    providers: [
      UniverseScreenStore,
      {provide: PortfolioConstructionService, useValue: service},
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
});
