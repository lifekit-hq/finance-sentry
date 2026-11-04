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

  it('lists each move with its linked news and signal, and says so plainly when none', () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES});
    fixture.detectChanges();

    const card = byTestId('notable-moves');
    expect(card).not.toBeNull();
    const text = card?.textContent ?? '';
    expect(text).toContain('+8.2%');
    expect(text).toContain('-6.1%');
    expect(card?.querySelector('a[href="https://example.com/a"]')).not.toBeNull();
    expect(card?.querySelector('a[href^="#"]')).toBeNull();
    expect(text).toContain('no linked news or signal found');
  });

  it('shows a linked signal as plain text when the Radar signals card is not rendered', () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: MOVES, radarSignals: []});
    fixture.detectChanges();

    expect(byTestId('move-signal-link')).toBeNull();
    expect(byTestId('move-signal-text')?.textContent).toContain('Volume Spike');
    expect(byTestId('notable-moves')?.textContent).toContain('example-scanner');
  });

  it('scrolls to the Radar signals card, without navigating, when a signal is clicked', () => {
    mockStore.dossier.set({
      ...DOSSIER,
      notableMoves: MOVES,
      radarSignals: [
        {
          timestamp: '2026-10-21T09:00:00Z',
          scanner: 'volume',
          signalType: 'volume_spike',
          severity: 'notable',
          payload: {},
        },
      ],
    });
    fixture.detectChanges();

    const target = root().ownerDocument.getElementById('radar-signals');
    expect(target).not.toBeNull();
    const scrollSpy = vi.fn();
    if (target) {
      target.scrollIntoView = scrollSpy;
    }
    byTestId('move-signal-link')?.querySelector('button')?.click();

    expect(scrollSpy).toHaveBeenCalled();
  });

  it('shows nothing when no day reached the threshold', () => {
    mockStore.dossier.set({...DOSSIER, notableMoves: {...MOVES, moves: []}});
    fixture.detectChanges();

    expect(byTestId('notable-moves')).toBeNull();
  });
});
