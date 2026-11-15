import {type ChartRangeId} from '../../models/dossier/dossier.model';

/** The history is read whole; the price block cuts it to the period the reader picks. */
export const CHART_HISTORY_RANGE: ChartRangeId = 'MAX';

/** How many days after the asset's first close in a period the S&P 500 may start and still be drawn against it. */
export const BENCHMARK_START_TOLERANCE_DAYS = 5;

export const AVERAGE_COST_LABEL = 'Your average cost';

/** The price block's height (title, headline, plot, period tabs, caption), held while the history loads. */
export const CHART_SKELETON_HEIGHT = '26rem';
