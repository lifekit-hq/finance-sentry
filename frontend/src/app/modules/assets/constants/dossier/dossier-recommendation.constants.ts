import {type RecommendationBucketStyle} from '../../models/dossier/dossier.model';

/** Bar order, bullish to bearish; the winning side is named by `RECOMMENDATION_VERDICT_LABELS`. */
export const RECOMMENDATION_BUCKETS: readonly RecommendationBucketStyle[] = [
  {bucket: 'strongBuy', label: 'Strong buy', barClass: 'bg-status-success'},
  {bucket: 'buy', label: 'Buy', barClass: 'bg-status-success/60'},
  {bucket: 'hold', label: 'Hold', barClass: 'bg-text-secondary/40'},
  {bucket: 'sell', label: 'Sell', barClass: 'bg-status-error/60'},
  {bucket: 'strongSell', label: 'Strong sell', barClass: 'bg-status-error'},
];

export const RECOMMENDATION_BUY_LABEL = 'Buy';
export const RECOMMENDATION_HOLD_LABEL = 'Hold';
export const RECOMMENDATION_SELL_LABEL = 'Sell';
