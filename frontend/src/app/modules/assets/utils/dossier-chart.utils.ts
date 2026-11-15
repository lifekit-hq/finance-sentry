import {type PriceSeries} from '@lifekit-hq/charts-core';
import {type Nullable} from '@lifekit-hq/ui';

import {MoneyUtils} from '../../../shared/utils/money.utils';
import {AVERAGE_COST_LABEL} from '../constants/dossier/dossier-chart.constants';
import {type AssetChartDto} from '../models/dossier/dossier.model';
import {type DossierChartView} from '../models/dossier/dossier-view.model';

export class DossierChartUtils {
  /**
   * The whole history as the price block's series: the asset's closes, then the S&P 500 when the API
   * covers the window. The block cuts them to the period and reads percent while the index overlays.
   * Null when there is no history to draw.
   */
  public static view(chart: Nullable<AssetChartDto>): Nullable<DossierChartView> {
    if (!chart || chart.points.length === 0) {
      return null;
    }
    const times = chart.points.map(p => Date.parse(p.date));
    const series: PriceSeries[] = [
      {label: chart.symbol, points: chart.points.map((p, i) => ({time: times[i], value: p.close}))},
    ];

    const benchmark = DossierChartUtils.filledBenchmark(chart);
    if (benchmark) {
      series.push({
        label: chart.benchmarkLabel,
        points: benchmark.map((value, i) => ({time: times[i], value})),
      });
    }

    const averageCost = chart.averageCost;
    const averageCostLabel =
      averageCost !== null && averageCost > 0
        ? `${AVERAGE_COST_LABEL} ${MoneyUtils.format(averageCost, chart.currency)}`
        : null;

    return {
      series,
      caption: DossierChartUtils.caption(chart.symbol, benchmark !== null && chart.benchmarkLabel),
      averageCostLabel,
    };
  }

  /** What each line is and what the dotted one marks, for the block's caption. */
  public static caption(symbol: string, benchmarkLabel: string | false): string {
    return benchmarkLabel
      ? `${symbol} (first line) against ${benchmarkLabel} (second line), each as its change since the period began; the dotted line marks the start of the period.`
      : `${symbol} price (solid line); the dotted line marks the price at the start of the period.`;
  }

  /**
   * The benchmark as a complete line: a day the index had no close carries the previous close
   * (leading gaps take the first one). Null when the API reports it does not cover the window
   * (`benchmarkChangePct` null), so the line and the percentage never disagree.
   */
  private static filledBenchmark(chart: AssetChartDto): Nullable<number[]> {
    if (chart.benchmarkChangePct === null) {
      return null;
    }
    const first = chart.points.find(p => p.benchmark !== null)?.benchmark ?? null;
    if (first === null) {
      return null;
    }
    let last = first;
    return chart.points.map(p => {
      if (p.benchmark !== null) {
        last = p.benchmark;
      }
      return last;
    });
  }
}
