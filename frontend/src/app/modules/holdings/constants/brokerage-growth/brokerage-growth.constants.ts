import {
  type GrowthSleeve,
  type GrowthSleeveCopy,
} from '../../models/brokerage-growth/brokerage-growth.model';

/** IBKR reports deposits only once the Flex Query includes them, so its missing-flows note says how. */
export const IBKR_PROVIDER = 'ibkr';

/** A return reads to one decimal place: "+4.2%". */
export const RETURN_PERCENT_DIGITS = 1;

/** A return arrives as a fraction; the card reads it in percentage points. */
export const PERCENT_POINTS = 100;

/** The sleeve a card shows when its host names none. */
export const DEFAULT_GROWTH_SLEEVE: GrowthSleeve = 'brokerage';

export const GROWTH_SLEEVE_COPY: Readonly<Record<GrowthSleeve, GrowthSleeveCopy>> = {
  brokerage: {
    title: 'Performance',
    scope: 'Interactive Brokers and Inzhur, cash included',
    includes: 'Growth includes dividends and interest.',
    badgeLabel: 'Brokerage growth',
  },
  crypto: {
    title: 'Crypto performance',
    scope: 'Binance and Revolut X',
    includes: 'Growth includes rewards and is after fees.',
    badgeLabel: 'Crypto growth',
  },
};
