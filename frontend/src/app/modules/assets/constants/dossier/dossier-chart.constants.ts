import {type ChartColour} from '../../../../shared/models/chart-colour/chart-colour.model';
import {type ChartRangeId} from '../../models/dossier/dossier.model';
import {type ChartRangeOption} from '../../models/dossier/dossier-view.model';

/** The timeframe tabs over the price chart (#824), in display order. */
export const CHART_RANGES: readonly ChartRangeOption[] = [
  {label: '1M', value: '1M'},
  {label: '6M', value: '6M'},
  {label: 'YTD', value: 'YTD'},
  {label: '1Y', value: '1Y'},
  {label: '5Y', value: '5Y'},
  {label: 'Max', value: 'MAX'},
];

export const DEFAULT_CHART_RANGE: ChartRangeId = '1Y';

/** The asset's own line leads the series ramp; the comparison marks stay off it. */
export const PRICE_LINE_COLOUR: ChartColour = {step: 1};
export const BENCHMARK_LINE_COLOUR: ChartColour = {step: 4};
export const AVERAGE_COST_LINE_COLOUR: ChartColour = {step: 6};

export const PRICE_LINE_LABEL = 'Price';
export const AVERAGE_COST_LABEL = 'Your average cost';

/** The library chart card's height (label, 16rem canvas, padding), held while the range loads. */
export const CHART_SKELETON_HEIGHT = '20rem';
