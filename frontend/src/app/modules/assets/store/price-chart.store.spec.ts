import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, type ParamMap} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {BehaviorSubject, NEVER, of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {AuthStore} from '../../auth/store/auth.store';
import {
  type AssetPriceBarsDto,
  type PriceBarDto,
  type PriceIntervalId,
  type PricePeriodId,
} from '../models/price-chart/price-chart.model';
import {DossierService} from '../services/dossier.service';
import {PriceChartDataUtils} from '../utils/price-chart-data.utils';
import {PriceChartSelectionUtils} from '../utils/price-chart-selection.utils';
import {PriceChartStore} from './price-chart.store';

const DAY = 86_400;
const START = 1_760_000_000;

function bars(count: number): PriceBarDto[] {
  return Array.from({length: count}, (_, i) => ({
    time: START + i * DAY,
    offset: 0,
    open: 100 + i,
    high: 102 + i,
    low: 99 + i,
    close: 101 + i,
    volume: 1000 + i,
  }));
}

function dto(overrides: Partial<AssetPriceBarsDto> & {count?: number} = {}): AssetPriceBarsDto {
  const series = bars(overrides.count ?? 30);
  return {
    symbol: 'AAPL',
    period: '1Y',
    interval: '1d',
    intervals: ['1d', '1w'],
    currency: 'USD',
    timeZone: 'America/New_York',
    bars: series,
    windowStart: series[0].time,
    truncated: false,
    weekRange: {high: 200, low: 90, from: '2025-11-05', coversYear: true},
    source: 'yahoo:chart',
    retrievedAt: '2026-11-05T10:00:00Z',
    ...overrides,
  };
}

function httpError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 400, error: {errorCode}}));
}

describe('PriceChartStore', () => {
  let service: {getBars: ReturnType<typeof vi.fn>};
  let params: BehaviorSubject<ParamMap>;
  let userId: string;

  function inject(): InstanceType<typeof PriceChartStore> {
    TestBed.configureTestingModule({
      providers: [
        PriceChartStore,
        {provide: DossierService, useValue: service},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {provide: ActivatedRoute, useValue: {paramMap: params}},
        {provide: AuthStore, useValue: {userId: () => userId}},
      ],
    });
    return TestBed.inject(PriceChartStore);
  }

  beforeEach(() => {
    localStorage.clear();
    userId = 'user-1';
    params = new BehaviorSubject(convertToParamMap({symbol: 'AAPL'}));
    service = {
      getBars: vi
        .fn()
        .mockImplementation((_: string, period: PricePeriodId, interval: PriceIntervalId | null) =>
          of(dto({period, interval: interval ?? '1d'}))
        ),
    };
  });

  it("loads the route symbol's bars on the default period and adopts the candle size the server used", () => {
    const store = inject();
    TestBed.tick();

    expect(service.getBars).toHaveBeenCalledWith('AAPL', '1Y', null);
    expect(store.bars()?.bars).toHaveLength(30);
    expect(store.interval()).toBe('1d');
    expect(store.asyncStatus()).toBe('success');
  });

  it('reloads on a new period with the candle size back on its default', () => {
    const store = inject();
    TestBed.tick();
    store.selectInterval('1w');
    store.selectPeriod('3M');

    expect(service.getBars).toHaveBeenLastCalledWith('AAPL', '3M', null);
    expect(store.period()).toBe('3M');
  });

  it('does not reload for the period or candle size already shown', () => {
    const store = inject();
    TestBed.tick();
    service.getBars.mockClear();

    store.selectPeriod('1Y');
    store.selectInterval('1d');

    expect(service.getBars).not.toHaveBeenCalled();
  });

  it('asks for the picked candle size', () => {
    const store = inject();
    TestBed.tick();
    store.selectInterval('1w');

    expect(service.getBars).toHaveBeenLastCalledWith('AAPL', '1Y', '1w');
  });

  it('ignores a slow response for a period the reader already moved past', () => {
    const slow = new Subject<AssetPriceBarsDto>();
    service.getBars.mockImplementation((_: string, period: PricePeriodId) =>
      period === '5D' ? slow : of(dto({period, count: 12}))
    );
    const store = inject();
    TestBed.tick();

    store.selectPeriod('5D');
    store.selectPeriod('6M');
    slow.next(dto({period: '5D', count: 99}));

    expect(store.bars()?.period).toBe('6M');
    expect(store.bars()?.bars).toHaveLength(12);
  });

  it('shows the error message for a rejected request and keeps nothing stale as success', () => {
    service.getBars.mockReturnValue(httpError('INVALID_PRICE_BAR_REQUEST'));
    const store = inject();
    TestBed.tick();

    expect(store.asyncStatus()).toBe('error');
    expect(store.barsErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['INVALID_PRICE_BAR_REQUEST']);
  });

  it('drops the old symbol and loads the new one when the route symbol changes in place', () => {
    const store = inject();
    TestBed.tick();
    store.setMeasured({from: START, to: START + DAY});

    service.getBars.mockReturnValue(NEVER);
    params.next(convertToParamMap({symbol: 'ZZZZ'}));
    TestBed.tick();

    expect(service.getBars).toHaveBeenLastCalledWith('ZZZZ', '1Y', null);
    expect(store.bars()).toBeNull();
    expect(store.measured()).toBeNull();
  });

  describe('remembered selection', () => {
    it('restores the kind and averages left on for this user', () => {
      PriceChartSelectionUtils.write('user-1', {kind: 'area', movingAverages: ['sma200']});
      const store = inject();
      TestBed.tick();

      expect(store.kind()).toBe('area');
      expect(store.movingAverages()).toEqual(['sma200']);
    });

    it("does not read another user's selection", () => {
      PriceChartSelectionUtils.write('someone-else', {kind: 'line', movingAverages: ['ema9']});
      const store = inject();
      TestBed.tick();

      expect(store.kind()).toBe('candles');
      expect(store.movingAverages()).toEqual([]);
    });

    it('stores a toggled average and the kind, so the next visit finds them', () => {
      const store = inject();
      TestBed.tick();
      store.toggleMovingAverage('sma200');
      store.toggleMovingAverage('ema9');
      store.selectKind('line');

      expect(PriceChartSelectionUtils.read('user-1')).toEqual({
        kind: 'line',
        movingAverages: ['sma200', 'ema9'],
      });

      store.toggleMovingAverage('ema9');
      expect(PriceChartSelectionUtils.read('user-1').movingAverages).toEqual(['sma200']);
    });
  });

  describe('derived views', () => {
    it('draws only the averages switched on', () => {
      const store = inject();
      TestBed.tick();
      store.toggleMovingAverage('sma20');

      expect(store.lines().map(l => l.spec.id)).toEqual(['sma20']);
      expect(store.lines()[0].points).toHaveLength(11);
      expect(store.gaps()).toEqual([]);
    });

    it('says why an average on draws nothing when the source has too few bars', () => {
      const store = inject();
      TestBed.tick();
      store.toggleMovingAverage('sma200');

      expect(store.lines()[0].points).toEqual([]);
      expect(store.gapNote()).toBe('Not drawn: SMA 200 needs 200 bars (30 available).');
    });

    it('greys out an average the bars are too few for, but never one already on', () => {
      const store = inject();
      TestBed.tick();
      store.toggleMovingAverage('sma100');

      const byId = Object.fromEntries(store.averageOptions().map(o => [o.value, o.disabled]));
      expect(byId['sma20']).toBe(false);
      expect(byId['sma100']).toBe(false);
      expect(byId['sma200']).toBe(true);
      expect(store.averageHint()).not.toBeNull();
    });

    it('offers only the candle sizes the period can be drawn at', () => {
      const store = inject();
      TestBed.tick();

      const disabled = store
        .intervalOptions()
        .filter(o => o.disabled)
        .map(o => o.value);
      expect(disabled).toEqual(['5m', '15m', '1h', '1mo']);
    });

    it('states the move over the view, from the window until a pan or zoom reports one', () => {
      const store = inject();
      TestBed.tick();

      expect(store.visibleChange()).toMatchObject({fromPrice: 101, toPrice: 130, bars: 30});

      store.setVisible({from: START + 10 * DAY, to: START + 20 * DAY});
      expect(store.visibleChange()).toMatchObject({fromPrice: 111, toPrice: 121, bars: 11});
      expect(store.visibleReadout()?.text).toBe('+$10.00 (+9.0%) over 10 days');
      expect(store.visibleReadout()?.isGain).toBe(true);
    });

    it('reads a loss as a loss', () => {
      service.getBars.mockReturnValue(
        of(
          dto({
            bars: [
              {time: START, offset: 0, open: 10, high: 10, low: 8, close: 10, volume: 1},
              {time: START + DAY, offset: 0, open: 9, high: 9, low: 7, close: 8, volume: 1},
            ],
            windowStart: START,
          })
        )
      );
      const store = inject();
      TestBed.tick();

      expect(store.visibleReadout()).toEqual({text: '-$2.00 (-20.0%) over 1 day', isGain: false});
    });

    it('states the move across a measured span and clears it with the period', () => {
      const store = inject();
      TestBed.tick();
      store.setMeasured({from: START + 2 * DAY, to: START + 6 * DAY});

      expect(store.measureChange()).toMatchObject({fromPrice: 103, toPrice: 107, bars: 5});
      expect(store.measureReadout()?.text).toBe('+$4.00 (+3.9%) over 4 days');

      store.selectPeriod('6M');
      expect(store.measured()).toBeNull();
      expect(store.measureReadout()).toBeNull();
    });

    it('turning Measure off drops the span', () => {
      const store = inject();
      TestBed.tick();
      store.setMeasuring(true);
      store.setMeasured({from: START, to: START + 3 * DAY});
      store.setMeasuring(false);

      expect(store.isMeasuring()).toBe(false);
      expect(store.measured()).toBeNull();
    });

    it('reads the legend from the bar under the crosshair, else the last bar', () => {
      const store = inject();
      TestBed.tick();

      expect(store.legend()?.close).toBe('$130.00');

      store.setHoverTime(START + 3 * DAY);
      expect(store.legend()?.close).toBe('$104.00');
      expect(store.legend()?.volume).toBe('1K');

      store.setHoverTime(null);
      expect(store.legend()?.close).toBe('$130.00');
    });

    describe('across a fall-back clock change', () => {
      // Europe/London left BST on 2026-10-25: 00:30 UTC and 01:30 UTC both read 01:30 on the wall clock.
      const bst = Date.UTC(2026, 9, 25, 0, 30) / 1000;
      const gmt = Date.UTC(2026, 9, 25, 1, 30) / 1000;
      const hour = (time: number, offset: number, close: number): PriceBarDto => ({
        time,
        offset,
        open: close,
        high: close,
        low: close,
        close,
        volume: 1,
      });

      it('keeps both repeated-hour bars on a strictly ascending, unique axis', () => {
        service.getBars.mockReturnValue(
          of(
            dto({
              interval: '1h',
              timeZone: 'Europe/London',
              bars: [hour(bst, 3600, 100), hour(gmt, 0, 110)],
              windowStart: bst,
            })
          )
        );
        const store = inject();
        TestBed.tick();

        const times = PriceChartDataUtils.closes(store.bars()?.bars ?? []).map(p => p.time);
        expect(times).toEqual([bst, gmt]);
        expect(times[0]).toBeLessThan(times[1]);
      });

      it('reads the legend of the bar hovered, not its twin on the wall clock', () => {
        service.getBars.mockReturnValue(
          of(
            dto({
              interval: '1h',
              timeZone: 'Europe/London',
              bars: [hour(bst, 3600, 100), hour(gmt, 0, 110)],
              windowStart: bst,
            })
          )
        );
        const store = inject();
        TestBed.tick();

        store.setHoverTime(bst);
        expect(store.legend()).toMatchObject({close: '$100.00', time: '25 Oct 2026, 01:30'});
        store.setHoverTime(gmt);
        expect(store.legend()).toMatchObject({close: '$110.00', time: '25 Oct 2026, 01:30'});
      });

      it('measures the real hour between them, not zero', () => {
        service.getBars.mockReturnValue(
          of(
            dto({
              interval: '1h',
              timeZone: 'Europe/London',
              bars: [hour(bst, 3600, 100), hour(gmt, 0, 110)],
              windowStart: bst,
            })
          )
        );
        const store = inject();
        TestBed.tick();
        store.setMeasured({from: bst, to: gmt});

        expect(store.measureChange()).toMatchObject({seconds: 3600, bars: 2, change: 10});
      });
    });

    it('names the 52-week range, and says so when the series is younger', () => {
      const store = inject();
      TestBed.tick();
      expect(store.weekRangeText()).toBe('52-week range $90.00 - $200.00');
    });

    it('says the history starts late instead of showing a short period unexplained', () => {
      service.getBars.mockReturnValue(of(dto({truncated: true})));
      const store = inject();
      TestBed.tick();

      expect(store.truncatedNote()).toContain('Price history starts');
    });

    it('reports an asset with no bars as empty, not as an error', () => {
      service.getBars.mockReturnValue(of(dto({bars: [], weekRange: null, windowStart: 0})));
      const store = inject();
      TestBed.tick();

      expect(store.isEmpty()).toBe(true);
      expect(store.asyncStatus()).toBe('success');
      expect(store.summary()).toBe('Price chart: no price history.');
    });

    it('keeps the chart on screen, dimmed, while a new period loads', () => {
      const store = inject();
      TestBed.tick();
      service.getBars.mockReturnValue(NEVER);
      store.selectPeriod('5Y');

      expect(store.isReloading()).toBe(true);
      expect(store.asyncStatus()).toBe('success');
      expect(store.bars()).not.toBeNull();
    });

    it('summarises the chart for a screen reader', () => {
      const store = inject();
      TestBed.tick();

      expect(store.summary()).toContain('AAPL price chart, 1Y in Day candles.');
      expect(store.summary()).toContain('Last close $130.00.');
    });
  });
});
