import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {ASYNC_STATE_SHOW_DELAY_MS} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {describe, expect, it, onTestFinished, vi} from 'vitest';

import {type ConcentrationHistory} from '../../models/risk-picture/risk-picture.model';
import {RiskPictureService} from '../../services/risk-picture.service';
import {ConcentrationHistoryComponent} from './concentration-history.component';

describe('ConcentrationHistoryComponent', () => {
  const render = async (
    history: ConcentrationHistory
  ): Promise<ComponentFixture<ConcentrationHistoryComponent>> => {
    TestBed.configureTestingModule({
      providers: [
        {provide: RiskPictureService, useValue: {getConcentrationHistory: () => of(history)}},
      ],
    });
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    const fixture = TestBed.createComponent(ConcentrationHistoryComponent);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(ASYNC_STATE_SHOW_DELAY_MS + 1);
    fixture.detectChanges();
    return fixture;
  };

  it('says there is too little history instead of drawing a chart', async () => {
    const fixture = await render({
      points: [],
      cryptoTarget: 0.1,
      cashTarget: null,
      reason: 'NotEnoughHistory',
    });

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="concentration-too-little"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="concentration-chart"]')).toBeNull();
  });

  it('shows the latest figure against the IPS target for the selected measure', async () => {
    const fixture = await render({
      points: [
        {date: '2026-01-05', top3Share: 0.8, cryptoShare: 0.1, cashShare: null},
        {date: '2026-06-05', top3Share: 0.85, cryptoShare: 0.15, cashShare: null},
      ],
      cryptoTarget: 0.1,
      cashTarget: null,
      reason: null,
    });
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="concentration-summary"]')?.textContent).toContain(
      'Top three share: Now 85.0%'
    );

    fixture.componentInstance.chooseMeasure('crypto');
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="concentration-summary"]')?.textContent).toContain(
      'drift +5.0 pts'
    );
    expect(el.querySelector('[data-testid="concentration-target"]')).toBeTruthy();

    fixture.componentInstance.chooseMeasure('top3');
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="concentration-chart"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="concentration-target"]')).toBeNull();
  });
});
