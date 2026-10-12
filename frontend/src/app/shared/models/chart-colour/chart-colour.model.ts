/** Colours that carry a meaning and so never follow the theme seed (`@lifekit-hq/tokens` 3.1). */
export type FixedColourName =
  'asset-equity' | 'asset-crypto' | 'asset-cash' | 'gain' | 'loss' | 'flow-in' | 'flow-out';

/** A chart mark's colour: a fixed-meaning token, or a position in the seed's series ramp. */
export type ChartColour = {readonly fixed: FixedColourName} | {readonly step: number};
