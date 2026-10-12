import {NEUTRAL_STEP} from '@lifekit-hq/charts-core';

import {type ChartColour} from '../../models/chart-colour/chart-colour.model';

/** Asset classes and the account sleeves that mirror them keep one colour in every chart and legend. */
export const EQUITY_COLOUR: ChartColour = {fixed: 'asset-equity'};
export const CRYPTO_COLOUR: ChartColour = {fixed: 'asset-crypto'};
export const CASH_COLOUR: ChartColour = {fixed: 'asset-cash'};

/** Money coming in and going out of the books. */
export const FLOW_IN_COLOUR: ChartColour = {fixed: 'flow-in'};
export const FLOW_OUT_COLOUR: ChartColour = {fixed: 'flow-out'};

/** "Other" / muted comparison marks carry no meaning, so they stay on the theme's neutral step. */
export const NEUTRAL_COLOUR: ChartColour = {step: NEUTRAL_STEP};

/** Tailwind text classes for a change of value (P&L, day change, net-worth pace). */
export const GAIN_TEXT_CLASS = 'text-gain';
export const LOSS_TEXT_CLASS = 'text-loss';
export const FLOW_IN_TEXT_CLASS = 'text-flow-in';
