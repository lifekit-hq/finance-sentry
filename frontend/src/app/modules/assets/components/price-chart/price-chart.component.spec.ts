import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {CmnShellService} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it} from 'vitest';

import {PriceChartStore} from '../../store/price-chart.store';
import {PriceChartCanvasComponent} from '../price-chart-canvas/price-chart-canvas.component';
import {PriceChartComponent} from './price-chart.component';

const TRADINGVIEW_URL = 'https://www.tradingview.com/';

@Component({
  selector: 'fns-price-chart-canvas',
  template: '<canvas data-testid="canvas-stub"></canvas>',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class CanvasStubComponent {}

describe('PriceChartComponent', () => {
  const storeStub = {
    asyncStatus: signal('success'),
    barsErrorMessage: signal(null),
    isEmpty: signal(false),
    bars: signal({}),
    kind: signal('candles'),
    period: signal('3M'),
    interval: signal('1d'),
    intervalOptions: signal([]),
    averageOptions: signal([]),
    movingAverages: signal([]),
    isMeasuring: signal(false),
    isReloading: signal(false),
    measured: signal(null),
    summary: signal('AAPL price chart'),
    visibleReadout: signal(null),
    legend: signal(null),
    lines: signal([]),
    measureReadout: signal(null),
    weekRangeText: signal(null),
    truncatedNote: signal(null),
    gapNote: signal(null),
    averageHint: signal(null),
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{provide: CmnShellService, useValue: {isPhone: signal(false)}}],
    }).overrideComponent(PriceChartComponent, {
      remove: {imports: [PriceChartCanvasComponent]},
      add: {imports: [CanvasStubComponent]},
      set: {providers: [{provide: PriceChartStore, useValue: storeStub}]},
    });
  });

  it('credits TradingView in the notes under the chart, outside the canvas, as a link to tradingview.com', () => {
    const fixture = TestBed.createComponent(PriceChartComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    const link = host.querySelector<HTMLAnchorElement>('[data-testid="chart-attribution"] a');
    expect(link?.getAttribute('href')).toBe(TRADINGVIEW_URL);
    expect(link?.textContent).toContain('TradingView');

    const canvas = host.querySelector('fns-price-chart-canvas') as HTMLElement;
    const notes = host.querySelector('[data-testid="chart-notes"]') as HTMLElement;
    expect(canvas.contains(link)).toBe(false);
    expect(notes.contains(link)).toBe(true);
    expect(
      canvas.compareDocumentPosition(notes) & Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();
  });
});
