import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {type Nullable, ThemeService} from '@lifekit-hq/ui';
import {
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type LogicalRange,
  type MouseEventParams,
  type SeriesType,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';

import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {ColourAlphaUtils} from '../../../../shared/utils/colour-alpha.utils';
import {
  PRICE_CHART_HEIGHT_PX,
  PRICE_LINE_COLOUR,
  PRICE_PANE_STRETCH,
  RIGHT_OFFSET_BARS,
  SELECTION_ALPHA,
  VOLUME_PANE_STRETCH,
  WEEK_RANGE_COLOUR,
} from '../../constants/price-chart/price-chart.constants';
import {
  type AssetPriceBarsDto,
  type MovingAverageId,
  type MovingAverageLine,
  type PriceChartKind,
  type TimeSpan,
} from '../../models/price-chart/price-chart.model';
import {PriceChartDataUtils} from '../../utils/price-chart-data.utils';
import {PriceChartViewUtils} from '../../utils/price-chart-view.utils';

type ChartLib = typeof import('lightweight-charts');

interface Rect {
  left: number;
  width: number;
}

const PRICE_PANE = 0;
const VOLUME_PANE = 1;
const LINE_WIDTH = 2;
const MA_LINE_WIDTH = 1;
const WEEK_LINE_DASH_WIDTH = 1;
const AREA_TOP_ALPHA = 0.28;
const AREA_BOTTOM_ALPHA = 0;
const MIN_MEASURE_BARS = 2;
const DECIMAL_BASE = 10;
const HALF_BAR = 0.5;
/** What moves the chart by hand: after any of these the viewer's view is theirs, not the period's window. */
const UNPIN_EVENTS = ['wheel', 'mousedown', 'touchstart', 'keydown'] as const;

/**
 * The charting library behind the dossier's interactive price chart: draws the bars as candles, a line or an area,
 * the moving-average lines, the 52-week lines and a volume pane, and reports the bars in view, the bar under the
 * crosshair and the span picked with the measure tool. It holds no data of its own: everything is an input, and it
 * is the one place imperative DOM work lives (the canvas has no declarative form). `lightweight-charts` is imported
 * on first render, so the library is a lazy chunk and never part of the initial bundle.
 */
@Component({
  selector: 'fns-price-chart-canvas',
  templateUrl: './price-chart-canvas.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'relative block'},
})
export class PriceChartCanvasComponent {
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly theme = toSignal(inject(ThemeService).activeTheme$, {initialValue: null});
  private readonly destroyRef = inject(DestroyRef);

  private readonly lib = signal<Nullable<ChartLib>>(null);
  private chart: Nullable<IChartApi> = null;
  private priceSeries: Nullable<ISeriesApi<SeriesType>> = null;
  private priceKind: Nullable<PriceChartKind> = null;
  private volumeSeries: Nullable<ISeriesApi<'Histogram'>> = null;
  private weekLines: IPriceLine[] = [];
  private readonly averageSeries = new Map<MovingAverageId, ISeriesApi<'Line'>>();
  private drawnBars: Nullable<AssetPriceBarsDto> = null;
  private lastHover: Nullable<number> = null;
  private lastVisible: Nullable<TimeSpan> = null;
  /** True from a new window being shown until the viewer pans or zooms: a resize in that time re-fits the window. */
  private windowPinned = false;
  private resizeObserver: Nullable<ResizeObserver> = null;
  private dragFrom: Nullable<number> = null;
  private selectedSpan: Nullable<{from: number; to: number}> = null;
  private readonly viewTick = signal(0);

  public readonly bars = input.required<AssetPriceBarsDto>();
  public readonly kind = input.required<PriceChartKind>();
  public readonly lines = input.required<MovingAverageLine[]>();
  /** True while the measure tool is armed: the overlay takes the pointer so a drag selects instead of panning. */
  public readonly isMeasuring = input(false);
  public readonly label = input('');
  /** The span the store holds as measured; when it is cleared outside (Clear, a new period) the box goes too. */
  public readonly measuredSpan = input<Nullable<TimeSpan>>(null);

  public readonly visibleChange = output<TimeSpan>();
  public readonly hoverChange = output<Nullable<number>>();
  public readonly measured = output<Nullable<TimeSpan>>();

  public readonly heightPx = PRICE_CHART_HEIGHT_PX;
  public readonly selection = signal<Nullable<Rect>>(null);
  public readonly selectionStyle = signal<Record<string, string>>({});

  constructor() {
    afterNextRender(() => void this.create());

    effect(() => {
      const lib = this.lib();
      if (lib && this.chart) {
        this.draw(lib, this.bars(), this.kind(), this.lines());
      }
    });

    effect(() => {
      const lib = this.lib();
      this.theme();
      if (lib && this.chart) {
        // The new theme's variables are applied by the time the next frame paints.
        requestAnimationFrame(() => this.applyTheme(lib));
      }
    });

    effect(() => {
      if (this.measuredSpan() === null && this.selectedSpan !== null && this.dragFrom === null) {
        this.selectedSpan = null;
        this.selection.set(null);
      }
    });

    this.destroyRef.onDestroy(() => this.destroy());
  }

  private readonly rangeHandler = (range: LogicalRange | null): void => this.onRangeChange(range);
  private readonly crosshairHandler = (param: MouseEventParams<Time>): void =>
    this.onCrosshair(param);
  private readonly shiftDown = (event: MouseEvent): void => this.onShiftMouseDown(event);
  private readonly unpinWindow = (): void => {
    this.windowPinned = false;
  };

  public onOverlayPointerDown(event: PointerEvent): void {
    if (!this.isMeasuring() || event.button !== 0) {
      return;
    }
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.beginDrag(event.clientX);
    event.preventDefault();
  }

  public onOverlayPointerMove(event: PointerEvent): void {
    this.moveDrag(event.clientX);
  }

  public onOverlayPointerUp(): void {
    this.endDrag();
  }

  public onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.selectedSpan) {
      this.clearMeasure();
    }
  }

  private async create(): Promise<void> {
    const lib = await import('lightweight-charts');
    const element = this.host().nativeElement;
    this.chart = lib.createChart(element, {
      autoSize: true,
      handleScale: {axisPressedMouseMove: true},
      rightPriceScale: {borderVisible: false},
      timeScale: {
        borderVisible: false,
        rightOffset: RIGHT_OFFSET_BARS,
        secondsVisible: false,
        // The card is laid out after the first draw: keep the window on screen while it settles to its width.
        lockVisibleTimeRangeOnResize: true,
      },
      crosshair: {mode: lib.CrosshairMode.Normal},
    });
    this.chart.timeScale().subscribeVisibleLogicalRangeChange(this.rangeHandler);
    this.chart.subscribeCrosshairMove(this.crosshairHandler);
    // Shift + drag measures on a mouse; the capture phase gets there before the chart's own pan handler.
    element.addEventListener('mousedown', this.shiftDown, true);
    // The card reaches its final width after the first draw (and again on a phone's rotation): until the viewer has
    // moved the chart, keep the period's window fitted to whatever width it settles at.
    for (const type of UNPIN_EVENTS) {
      element.addEventListener(type, this.unpinWindow, {capture: true, passive: true});
    }
    this.resizeObserver = new ResizeObserver(() => this.refitWindow());
    this.resizeObserver.observe(element);
    this.applyTheme(lib);
    this.lib.set(lib);
  }

  private destroy(): void {
    const element = this.host().nativeElement;
    element.removeEventListener('mousedown', this.shiftDown, true);
    for (const type of UNPIN_EVENTS) {
      element.removeEventListener(type, this.unpinWindow, true);
    }
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    this.chart?.timeScale().unsubscribeVisibleLogicalRangeChange(this.rangeHandler);
    this.chart?.unsubscribeCrosshairMove(this.crosshairHandler);
    this.chart?.remove();
    this.chart = null;
    this.priceSeries = null;
    this.volumeSeries = null;
    this.averageSeries.clear();
    this.weekLines = [];
  }

  private applyTheme(lib: ChartLib): void {
    const chart = this.chart;
    if (!chart) {
      return;
    }
    const text = this.token('--color-text-secondary', '#46565e');
    chart.applyOptions({
      layout: {
        background: {type: lib.ColorType.Solid, color: 'rgba(0, 0, 0, 0)'},
        textColor: text,
        fontFamily: this.token('--font-sans', 'system-ui, sans-serif'),
        attributionLogo: true,
      },
      grid: {
        vertLines: {color: this.token('--color-chart-grid', '#d6dde0')},
        horzLines: {color: this.token('--color-chart-grid', '#d6dde0')},
      },
      crosshair: {
        vertLine: {
          color: text,
          labelBackgroundColor: this.token('--color-surface-raised', '#e8edef'),
        },
        horzLine: {
          color: text,
          labelBackgroundColor: this.token('--color-surface-raised', '#e8edef'),
        },
      },
    });
    // The series colours come from the same tokens, so a theme change restyles them too.
    this.restyleSeries(lib);
    const accent = ChartColorUtils.canvas(PRICE_LINE_COLOUR);
    this.selectionStyle.set({
      background: ColourAlphaUtils.withAlpha(accent, SELECTION_ALPHA),
      'border-color': accent,
    });
  }

  private token(name: string, fallback: string): string {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
  }

  /** (Re)builds what is on the chart from the inputs. */
  private draw(
    lib: ChartLib,
    bars: AssetPriceBarsDto,
    kind: PriceChartKind,
    lines: MovingAverageLine[]
  ): void {
    const chart = this.chart;
    if (!chart) {
      return;
    }
    const data = bars.bars;

    this.drawPrice(lib, chart, data, kind);
    this.drawAverages(lib, chart, lines);
    this.drawVolume(lib, chart, data);
    this.drawWeekRange(lib, bars);
    this.restyleSeries(lib);

    chart.priceScale('right').applyOptions({scaleMargins: {top: 0.08, bottom: 0.06}});
    const panes = chart.panes();
    if (panes.length > 1) {
      panes[PRICE_PANE].setStretchFactor(PRICE_PANE_STRETCH);
      panes[VOLUME_PANE].setStretchFactor(VOLUME_PANE_STRETCH);
    }
    chart.applyOptions({
      timeScale: {
        timeVisible: bars.interval === '5m' || bars.interval === '15m' || bars.interval === '1h',
      },
    });
    // A new set of bars (period, candle size, symbol) puts the view back on its window and drops the span measured on
    // the old ones; toggling an average or the kind redraws the same bars and leaves both alone.
    if (bars !== this.drawnBars) {
      this.drawnBars = bars;
      this.showWindow(bars);
      this.clearMeasure();
    }
  }

  private drawPrice(
    lib: ChartLib,
    chart: IChartApi,
    data: AssetPriceBarsDto['bars'],
    kind: PriceChartKind
  ): void {
    if (this.priceKind !== kind && this.priceSeries) {
      chart.removeSeries(this.priceSeries);
      this.priceSeries = null;
      this.weekLines = [];
    }
    const precision = PriceChartViewUtils.precision(data);
    const priceFormat = {
      type: 'price' as const,
      precision,
      minMove: Number((1 / DECIMAL_BASE ** precision).toFixed(precision)),
    };
    if (!this.priceSeries) {
      this.priceSeries =
        kind === 'candles'
          ? chart.addSeries(lib.CandlestickSeries, {priceFormat}, PRICE_PANE)
          : kind === 'line'
            ? chart.addSeries(lib.LineSeries, {priceFormat, lineWidth: LINE_WIDTH}, PRICE_PANE)
            : chart.addSeries(lib.AreaSeries, {priceFormat, lineWidth: LINE_WIDTH}, PRICE_PANE);
      this.priceKind = kind;
    } else {
      this.priceSeries.applyOptions({priceFormat});
    }
    const points =
      kind === 'candles' ? PriceChartDataUtils.candles(data) : PriceChartDataUtils.closes(data);
    (this.priceSeries as ISeriesApi<'Line'>).setData(
      points.map(p => ({...p, time: p.time as UTCTimestamp}))
    );
  }

  private drawAverages(lib: ChartLib, chart: IChartApi, lines: MovingAverageLine[]): void {
    const wanted = new Set(lines.map(l => l.spec.id));
    for (const [id, series] of this.averageSeries) {
      if (!wanted.has(id)) {
        chart.removeSeries(series);
        this.averageSeries.delete(id);
      }
    }
    for (const line of lines) {
      let series = this.averageSeries.get(line.spec.id);
      if (!series) {
        series = chart.addSeries(
          lib.LineSeries,
          {
            lineWidth: MA_LINE_WIDTH,
            lastValueVisible: false,
            priceLineVisible: false,
            crosshairMarkerVisible: false,
          },
          PRICE_PANE
        );
        this.averageSeries.set(line.spec.id, series);
      }
      series.setData(line.points.map(p => ({time: p.time as UTCTimestamp, value: p.value})));
    }
  }

  private drawVolume(lib: ChartLib, chart: IChartApi, data: AssetPriceBarsDto['bars']): void {
    if (!PriceChartDataUtils.hasVolume(data)) {
      if (this.volumeSeries) {
        chart.removeSeries(this.volumeSeries);
        this.volumeSeries = null;
      }
      return;
    }
    this.volumeSeries ??= chart.addSeries(
      lib.HistogramSeries,
      {priceFormat: {type: 'volume'}, priceLineVisible: false, lastValueVisible: false},
      VOLUME_PANE
    );
    this.volumeSeries.setData(
      PriceChartDataUtils.volumes(data, this.gain(), this.loss()).map(v => ({
        ...v,
        time: v.time as UTCTimestamp,
      }))
    );
  }

  private drawWeekRange(lib: ChartLib, bars: AssetPriceBarsDto): void {
    const series = this.priceSeries;
    if (!series) {
      return;
    }
    for (const line of this.weekLines) {
      series.removePriceLine(line);
    }
    this.weekLines = [];
    const week = bars.weekRange;
    if (!week) {
      return;
    }
    const lineColour = ChartColorUtils.canvas(WEEK_RANGE_COLOUR);
    const scope = week.coversYear ? '52w' : 'Range';
    for (const [price, title] of [
      [week.high, `${scope} high`],
      [week.low, `${scope} low`],
    ] as const) {
      this.weekLines.push(
        series.createPriceLine({
          price,
          title,
          color: lineColour,
          lineWidth: WEEK_LINE_DASH_WIDTH,
          lineStyle: lib.LineStyle.Dashed,
          axisLabelVisible: true,
        })
      );
    }
  }

  private restyleSeries(lib: ChartLib): void {
    const gain = this.gain();
    const loss = this.loss();
    const accent = ChartColorUtils.canvas(PRICE_LINE_COLOUR);
    if (this.priceKind === 'candles') {
      this.priceSeries?.applyOptions({
        upColor: gain,
        downColor: loss,
        borderUpColor: gain,
        borderDownColor: loss,
        wickUpColor: gain,
        wickDownColor: loss,
      });
    } else if (this.priceKind === 'line') {
      this.priceSeries?.applyOptions({color: accent});
    } else if (this.priceKind === 'area') {
      this.priceSeries?.applyOptions({
        lineColor: accent,
        topColor: ColourAlphaUtils.withAlpha(accent, AREA_TOP_ALPHA),
        bottomColor: ColourAlphaUtils.withAlpha(accent, AREA_BOTTOM_ALPHA),
      });
    }
    for (const line of this.lines()) {
      this.averageSeries
        .get(line.spec.id)
        ?.applyOptions({color: ChartColorUtils.canvas(line.spec.colour)});
    }
    const week = ChartColorUtils.canvas(WEEK_RANGE_COLOUR);
    for (const line of this.weekLines) {
      line.applyOptions({color: week});
    }
    if (this.volumeSeries && this.bars().bars.length > 0) {
      this.volumeSeries.setData(
        PriceChartDataUtils.volumes(this.bars().bars, gain, loss).map(v => ({
          ...v,
          time: v.time as UTCTimestamp,
        }))
      );
    }
    void lib;
  }

  private gain(): string {
    return ChartColorUtils.canvas({fixed: 'gain'});
  }

  private loss(): string {
    return ChartColorUtils.canvas({fixed: 'loss'});
  }

  /** Scrolls to the period's window; the warm-up bars before it stay one pan to the left. */
  private refitWindow(): void {
    if (this.windowPinned && this.drawnBars) {
      this.showWindow(this.drawnBars);
    }
  }

  private showWindow(bars: AssetPriceBarsDto): void {
    this.windowPinned = true;
    const first = Math.max(
      0,
      bars.bars.findIndex(b => b.time >= bars.windowStart)
    );
    this.chart?.timeScale().setVisibleLogicalRange({
      from: first - HALF_BAR,
      to: bars.bars.length - 1 + RIGHT_OFFSET_BARS,
    });
  }

  private onRangeChange(range: Nullable<LogicalRange>): void {
    const data = this.bars().bars;
    this.viewTick.update(n => n + 1);
    this.syncSelection();
    if (!range || data.length === 0) {
      return;
    }
    const first = Math.min(data.length - 1, Math.max(0, Math.ceil(range.from)));
    const last = Math.min(data.length - 1, Math.max(0, Math.floor(range.to)));
    const span = {from: data[first].time, to: data[Math.max(first, last)].time};
    if (this.lastVisible?.from !== span.from || this.lastVisible.to !== span.to) {
      this.lastVisible = span;
      this.visibleChange.emit(span);
    }
  }

  private onCrosshair(param: MouseEventParams<Time>): void {
    const time = typeof param.time === 'number' ? param.time : null;
    if (time !== this.lastHover) {
      this.lastHover = time;
      this.hoverChange.emit(time);
    }
  }

  private onShiftMouseDown(event: MouseEvent): void {
    if (!event.shiftKey || event.button !== 0) {
      return;
    }
    event.stopPropagation();
    event.preventDefault();
    this.beginDrag(event.clientX);
    const move = (e: MouseEvent): void => this.moveDrag(e.clientX);
    const up = (): void => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
      this.endDrag();
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  }

  private indexAt(clientX: number): Nullable<number> {
    const chart = this.chart;
    const count = this.bars().bars.length;
    if (!chart || count === 0) {
      return null;
    }
    const x = clientX - this.host().nativeElement.getBoundingClientRect().left;
    const logical = chart.timeScale().coordinateToLogical(x);
    return logical === null ? null : Math.min(count - 1, Math.max(0, Math.round(logical)));
  }

  private beginDrag(clientX: number): void {
    this.dragFrom = this.indexAt(clientX);
    this.selectedSpan = null;
    this.selection.set(null);
    this.measured.emit(null);
  }

  private moveDrag(clientX: number): void {
    const from = this.dragFrom;
    const to = this.indexAt(clientX);
    if (from === null || to === null) {
      return;
    }
    this.setSelection(Math.min(from, to), Math.max(from, to));
  }

  private endDrag(): void {
    this.dragFrom = null;
    const span = this.selectedSpan;
    if (span && this.indexSpan(span) < MIN_MEASURE_BARS) {
      this.clearMeasure();
    }
  }

  private clearMeasure(): void {
    const hadSelection = this.selectedSpan !== null;
    this.selectedSpan = null;
    this.selection.set(null);
    if (hadSelection) {
      this.measured.emit(null);
    }
  }

  private indexSpan(span: {from: number; to: number}): number {
    return span.to - span.from + 1;
  }

  private setSelection(first: number, last: number): void {
    const data = this.bars().bars;
    this.selectedSpan = {from: first, to: last};
    this.syncSelection();
    this.measured.emit({from: data[first].time, to: data[last].time});
  }

  /** Re-places the selection box on the bars it covers, after a pan, zoom or resize moved them. */
  private syncSelection(): void {
    const span = this.selectedSpan;
    const scale = this.chart?.timeScale();
    if (!span || !scale) {
      return;
    }
    const left = scale.logicalToCoordinate(span.from as never);
    const right = scale.logicalToCoordinate(span.to as never);
    this.selection.set(
      left === null || right === null ? null : {left, width: Math.max(1, right - left)}
    );
  }
}
