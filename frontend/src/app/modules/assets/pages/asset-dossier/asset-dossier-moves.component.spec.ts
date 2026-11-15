import {signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {DossierStore} from '../../store/dossier.store';
import {AssetDossierComponent} from './asset-dossier.component';
import {buildMockStore, DOSSIER} from './asset-dossier.fixtures';

describe('AssetDossierComponent: Why is it moving?', () => {
  let fixture: ComponentFixture<AssetDossierComponent>;
  let mockStore: ReturnType<typeof buildMockStore>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const byTestId = (id: string): HTMLElement | null =>
    root().querySelector<HTMLElement>(`[data-testid="${id}"]`);

  beforeEach(async () => {
    mockStore = buildMockStore();

    await TestBed.configureTestingModule({
      imports: [AssetDossierComponent],
      providers: [provideRouter([]), {provide: AuthStore, useValue: {canUseAi: signal(true)}}],
    })
      .overrideComponent(AssetDossierComponent, {
        set: {providers: [{provide: DossierStore, useValue: mockStore}]},
      })
      .compileComponents();

    fixture = TestBed.createComponent(AssetDossierComponent);
  });

  const MOVES = {
    thresholdPct: 5,
    windowDays: 1,
    visibleDays: 30,
    moves: [
      {
        date: '2026-10-20',
        returnPct: 8.2,
        links: [
          {
            kind: 'news' as const,
            timestamp: '2026-10-20T12:00:00Z',
            title: 'Memory prices climb',
            source: 'yahoo:DRAM',
            url: 'https://example.com/a',
            severity: null,
            relevance: 3,
          },
          {
            kind: 'signal' as const,
            timestamp: '2026-10-21T09:00:00Z',
            title: 'volume_spike',
            source: 'example-scanner',
            url: null,
            severity: 'Notable',
            relevance: 2,
          },
        ],
      },
      {date: '2026-10-14', returnPct: -6.1, links: []},
    ],
  };

  // The grid draws a tile's panel only while that tile is open.
  const openTile = async (id: string): Promise<void> => {
    fixture.detectChanges();
    fixture.componentInstance.openTile.set(id);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const SIGNAL = {
    timestamp: '2026-10-21T09:00:00Z',
    scanner: 'volume',
    signalType: 'volume_spike',
    severity: 'notable',
    payload: {},
  };

  it('makes each move a tile: the day, the size of the move and what stands around it', () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES});
    fixture.detectChanges();

    const moves = mockStore.factTiles().filter(tile => tile.id.startsWith('move-'));
    expect(moves).toEqual([
      {id: 'move-2026-10-20', label: 'Moved 20 Oct', headline: '+8.2% · news, signal'},
      {id: 'move-2026-10-14', label: 'Moved 14 Oct', headline: '-6.1% · no cause found'},
    ]);
    const tiles = byTestId('dossier-facts')?.textContent ?? '';
    expect(tiles).toContain('Moved 20 Oct');
    expect(tiles).toContain('-6.1% · no cause found');
  });

  it('opens a move to its magnitude and cause chips, then the linked items', async () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES});
    await openTile('move-2026-10-20');

    const panel = byTestId('notable-move');
    expect(panel).not.toBeNull();
    expect(byTestId('move-summary')?.textContent).toContain('+8.2%');
    expect(byTestId('move-cause-news')?.textContent).toContain('1 news');
    expect(byTestId('move-cause-signal')?.textContent).toContain('1 signal');
    expect(byTestId('move-summary')?.querySelectorAll('cmn-icon')).toHaveLength(3);
    expect(panel?.querySelector('a[href="https://example.com/a"]')).not.toBeNull();
    expect(panel?.querySelector('a[href^="#"]')).toBeNull();
  });

  it('says so plainly when nothing was found around a move', async () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES});
    await openTile('move-2026-10-14');

    expect(byTestId('move-summary')?.textContent).toContain('-6.1%');
    expect(byTestId('move-cause-news')).toBeNull();
    expect(byTestId('move-cause-signal')).toBeNull();
    expect(byTestId('notable-move')?.textContent).toContain('no linked news or signal found');
  });

  it('shows a linked signal as plain text when the Radar signals tile is not there', async () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES, radarSignals: []});
    await openTile('move-2026-10-20');

    expect(byTestId('move-signal-link')).toBeNull();
    expect(byTestId('move-signal-text')?.textContent).toContain('Volume Spike');
    expect(byTestId('notable-move')?.textContent).toContain('example-scanner');
  });

  it('opens the Radar signals tile, without navigating, when a signal is clicked', async () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES, radarSignals: [SIGNAL]});
    await openTile('move-2026-10-20');

    const grid = byTestId('dossier-facts');
    const scrollSpy = vi.fn();
    if (grid) {
      grid.scrollIntoView = scrollSpy;
    }
    byTestId('move-signal-link')?.querySelector('button')?.click();

    expect(fixture.componentInstance.openTile()).toBe('radar-signals');
    expect(scrollSpy).toHaveBeenCalled();
  });

  it('adds no move tile when no day reached the threshold', () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: {...MOVES, moves: []}});
    fixture.detectChanges();

    expect(mockStore.factTiles().some(tile => tile.id.startsWith('move-'))).toBe(false);
  });
});
