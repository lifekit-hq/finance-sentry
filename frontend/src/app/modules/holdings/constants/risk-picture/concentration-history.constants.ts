import {type ConcentrationMeasure} from '../../models/risk-picture/risk-picture.model';

export const CONCENTRATION_MEASURES: readonly {value: ConcentrationMeasure; label: string}[] = [
  {value: 'top3', label: 'Top three'},
  {value: 'crypto', label: 'Crypto'},
  {value: 'cash', label: 'Cash'},
];

export const CONCENTRATION_PERCENT_POINTS = 100;

export const CONCENTRATION_PERCENT_DIGITS = 1;

export const CONCENTRATION_DOMAIN_PADDING = 1;

export const CONCENTRATION_BASIS =
  'Shares of the invested book on each day the book was captured; the cash share is of the brokerage and crypto sources on that snapshot day. Targets are the Investment Policy Statement.';

export const CONCENTRATION_TOO_LITTLE_HISTORY =
  'Not enough history yet: the chart needs the book captured on at least two days.';

export const CONCENTRATION_NO_DATA = 'No days with a measured figure yet.';
