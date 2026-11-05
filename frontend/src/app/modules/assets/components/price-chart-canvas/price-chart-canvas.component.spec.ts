import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {ThemeService} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {MOVING_AVERAGES} from '../../constants/price-chart/price-chart.constants';
import {
  type AssetPriceBarsDto,
  type MovingAverageLine,
  type TimeSpan,
} from '../../models/price-chart/price-chart.model';
import {MovingAverageUtils} from '../../utils/moving-average.utils';
import {PriceChartCanvasComponent} from './price-chart-canvas.component';

const DAY = 86_400;
const START = 1_760_000_000;
const BAR_COUNT = 80;

function dto(volume = 1000): AssetPriceBarsDto {
  const bars = Array.from({length: BAR_COUNT}, (_, i) => ({
    time: START + i * DAY,
    offset: 0,
    open: 100 + i,
    high: 102 + i,
    low: 99 + i,
    close: 101 + i,
    volume: volume + i,
  }));
  return {
    symbol: 'AAPL',
    period: '3M',
    interval: '1d',
    intervals: ['1d', '1w'],
    currency: 'USD',
    timeZone: null,
    bars,
    windowStart: bars[30].time,
    truncated: false,
    weekRange: {high: 200, low: 90, from: '2025-10-09', coversYear: true},
    source: 'yahoo:chart',
    retrievedAt: '2026-11-05T10:00:00Z',
  };
}

describe('PriceChartCanvasComponent', () => {
  let fixture: ComponentFixture<PriceChartCanvasComponent>;
  let host: HTMLElement;
  let measured: (TimeSpan | null)[];
  let visible: TimeSpan[];

  async function render(
    bars: AssetPriceBarsDto = dto(),
    lines: MovingAverageLine[] = []
  ): Promise<void> {
    fixture.componentRef.setInput('bars', bars);
    fixture.componentRef.setInput('kind', 'candles');
    fixture.componentRef.setInput('lines', lines);
    fixture.componentRef.setInput('label', 'AAPL price chart');
    fixture.detectChanges();
    // The library is a lazy chunk: wait for it to land and draw.
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(visible.length).toBeGreaterThan(0);
    });
  }

  function overlay(): HTMLElement {
    return fixture.nativeElement.querySelector('[data-testid="price-chart-overlay"]');
  }

  function lastSpan(): TimeSpan {
    const span = measured.filter((m): m is TimeSpan => m !== null).pop();
    if (!span) {
      throw new Error('no span was measured');
    }
    return span;
  }

  function pointer(type: string, x: number): PointerEvent {
    const rect = host.getBoundingClientRect();
    return new PointerEvent(type, {
      clientX: rect.left + x,
      clientY: rect.top + rect.height / 2,
      pointerId: 1,
      button: 0,
      bubbles: true,
    });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ThemeService, useValue: {activeTheme$: of('light')}}],
    });
    fixture = TestBed.createComponent(PriceChartCanvasComponent);
    fixture.nativeElement.style.display = 'block';
    fixture.nativeElement.style.width = '720px';
    document.body.appendChild(fixture.nativeElement);
    host = fixture.nativeElement.querySelector('[data-testid="price-chart-canvas"]');
    measured = [];
    visible = [];
    fixture.componentInstance.measured.subscribe(span => measured.push(span));
    fixture.componentInstance.visibleChange.subscribe(span => visible.push(span));
  });

  it('draws the chart from its inputs and names it for a screen reader', async () => {
    await render();

    expect(host.getAttribute('role')).toBe('img');
    expect(host.getAttribute('aria-label')).toBe('AAPL price chart');
  });

  it('reports the bars in view, starting at the period window rather than the warm-up', async () => {
    const bars = dto();
    await render(bars);

    await vi.waitFor(() => {
      fixture.detectChanges();
      const span = visible[visible.length - 1];
      expect(span.from).toBeGreaterThanOrEqual(bars.bars[29].time);
      expect(span.to).toBe(bars.bars[BAR_COUNT - 1].time);
    });
  });

  it('draws moving-average lines, the kind and an empty volume without throwing', async () => {
    const bars = dto(0);
    const lines = MovingAverageUtils.lines(bars.bars, ['sma20', 'ema9']);
    await render(bars, lines);

    fixture.componentRef.setInput('kind', 'area');
    fixture.detectChanges();
    fixture.componentRef.setInput('kind', 'line');
    fixture.componentRef.setInput('lines', []);
    fixture.detectChanges();
    fixture.componentRef.setInput('kind', 'candles');
    fixture.detectChanges();

    expect(host.querySelectorAll('canvas').length).toBeGreaterThan(0);
    expect(MOVING_AVERAGES.length).toBeGreaterThan(0);
  });

  it('draws and reports the bars of a repeated wall-clock hour on their real times', async () => {
    // Europe/London left BST on 2026-10-25: 00:30 UTC and 01:30 UTC both read 01:30.
    const first = Date.UTC(2026, 9, 25, 0, 30) / 1000;
    const hourly = ['bst', 'gmt', 'gmt'].map((_, i) => ({
      time: first + i * 3600,
      offset: i === 0 ? 3600 : 0,
      open: 100,
      high: 101,
      low: 99,
      close: 100 + i,
      volume: 10,
    }));
    await render({...dto(), interval: '1h', bars: hourly, windowStart: first});

    await vi.waitFor(() => {
      fixture.detectChanges();
      const span = visible[visible.length - 1];
      expect(span).toEqual({from: first, to: first + 2 * 3600});
    });
  });

  describe('measure tool', () => {
    it('lets the chart take the pointer until Measure is armed', async () => {
      await render();

      expect(overlay().classList.contains('pointer-events-none')).toBe(true);

      fixture.componentRef.setInput('isMeasuring', true);
      fixture.detectChanges();

      expect(overlay().classList.contains('pointer-events-none')).toBe(false);
      expect(overlay().style.touchAction).toBe('none');
    });

    it('reports the span dragged across the bars and draws its box', async () => {
      const bars = dto();
      await render(bars);
      fixture.componentRef.setInput('isMeasuring', true);
      fixture.detectChanges();

      overlay().dispatchEvent(pointer('pointerdown', 200));
      overlay().dispatchEvent(pointer('pointermove', 420));
      overlay().dispatchEvent(pointer('pointerup', 420));
      fixture.detectChanges();

      const span = lastSpan();
      expect(span.from).toBeLessThan(span.to);
      expect(bars.bars.some(b => b.time === span.from)).toBe(true);
      expect(bars.bars.some(b => b.time === span.to)).toBe(true);
      expect(
        fixture.nativeElement.querySelector('[data-testid="price-chart-selection"]')
      ).not.toBeNull();
    });

    it('reads a drag to the left the same as one to the right', async () => {
      await render();
      fixture.componentRef.setInput('isMeasuring', true);
      fixture.detectChanges();

      overlay().dispatchEvent(pointer('pointerdown', 420));
      overlay().dispatchEvent(pointer('pointermove', 200));
      overlay().dispatchEvent(pointer('pointerup', 200));

      const span = lastSpan();
      expect(span.from).toBeLessThan(span.to);
    });

    it('measures with Shift + drag on a mouse, without arming Measure', async () => {
      await render();
      const rect = host.getBoundingClientRect();
      const at = (type: string, x: number, shiftKey = false) =>
        new MouseEvent(type, {
          clientX: rect.left + x,
          clientY: rect.top + 100,
          shiftKey,
          button: 0,
          bubbles: true,
          cancelable: true,
        });

      host.dispatchEvent(at('mousedown', 150, true));
      window.dispatchEvent(at('mousemove', 380));
      window.dispatchEvent(at('mouseup', 380));

      expect(measured.filter(Boolean).length).toBeGreaterThan(0);
    });

    it('does nothing for a plain drag, which pans the chart', async () => {
      await render();
      const rect = host.getBoundingClientRect();

      host.dispatchEvent(
        new MouseEvent('mousedown', {
          clientX: rect.left + 150,
          clientY: rect.top + 100,
          button: 0,
          bubbles: true,
        })
      );
      window.dispatchEvent(new MouseEvent('mouseup', {bubbles: true}));

      expect(measured.filter(Boolean)).toEqual([]);
    });

    it('drops a click that spans a single bar', async () => {
      await render();
      fixture.componentRef.setInput('isMeasuring', true);
      fixture.detectChanges();

      overlay().dispatchEvent(pointer('pointerdown', 300));
      overlay().dispatchEvent(pointer('pointerup', 300));
      fixture.detectChanges();

      expect(measured[measured.length - 1]).toBeNull();
      expect(
        fixture.nativeElement.querySelector('[data-testid="price-chart-selection"]')
      ).toBeNull();
    });

    it('clears the span on Escape', async () => {
      await render();
      fixture.componentRef.setInput('isMeasuring', true);
      fixture.detectChanges();
      overlay().dispatchEvent(pointer('pointerdown', 200));
      overlay().dispatchEvent(pointer('pointermove', 420));
      overlay().dispatchEvent(pointer('pointerup', 420));

      host.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));
      fixture.detectChanges();

      expect(measured[measured.length - 1]).toBeNull();
      expect(
        fixture.nativeElement.querySelector('[data-testid="price-chart-selection"]')
      ).toBeNull();
    });

    it('removes the box when the store clears the measured span', async () => {
      await render();
      fixture.componentRef.setInput('isMeasuring', true);
      fixture.detectChanges();
      overlay().dispatchEvent(pointer('pointerdown', 200));
      overlay().dispatchEvent(pointer('pointermove', 420));
      overlay().dispatchEvent(pointer('pointerup', 420));
      fixture.componentRef.setInput('measuredSpan', measured.filter(Boolean).pop() ?? null);
      fixture.detectChanges();

      fixture.componentRef.setInput('measuredSpan', null);
      fixture.detectChanges();

      expect(
        fixture.nativeElement.querySelector('[data-testid="price-chart-selection"]')
      ).toBeNull();
    });
  });

  it('tears the chart down with the component', async () => {
    await render();

    fixture.destroy();

    expect(host.querySelectorAll('canvas').length).toBe(0);
  });
});
