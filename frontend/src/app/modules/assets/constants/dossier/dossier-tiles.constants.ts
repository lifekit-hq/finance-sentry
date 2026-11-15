/** What a tile reads when the section has no figure to lead with. */
export const DEFAULT_TILE_HEADLINE = {
  valuation: 'Set your target',
} as const;

/** Tile labels and the words a headline is built from. */
export const TILE_HEADLINE = {
  analystsLabel: 'Analysts',
  thesisLabel: 'Plan',
  thesisActive: 'Active',
  thesisBroken: 'Invalidated',
  earningsLabel: 'Next earnings',
  newsLabel: 'News',
  signalsLabel: 'Signals',
  ownershipLabel: 'Ownership',
  ownershipUnavailable: 'No source',
  yourTarget: 'Your target',
  forwardPe: 'Fwd P/E',
} as const;

export const MOVE_TILE_DATE_FORMAT = 'd MMM';
export const MOVE_CAUSE_NEWS = 'news';
export const MOVE_CAUSE_SIGNAL = 'signal';
export const MOVE_CAUSE_NONE = 'no cause found';

/** The icons the move panel marks its magnitude and its causes with. */
export const MOVE_ICON = {
  up: 'TrendingUp',
  down: 'TrendingDown',
  news: 'Newspaper',
  signal: 'Radar',
} as const;
