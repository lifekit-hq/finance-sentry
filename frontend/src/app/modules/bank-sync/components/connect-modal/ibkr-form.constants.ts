/** IBKR self-service consumer keys are exactly 9 alphanumeric characters. */
export const CONSUMER_KEY_LENGTH = 9;

/** The three PEM artifacts uploaded as files and read into hidden form controls. */
export type PemControlName = 'signatureKey' | 'encryptionKey' | 'dhParam';

/** Flex Activity Query ids are plain numbers shown beside the query's name in Client Portal. */
export const FLEX_QUERY_ID_PATTERN = /^\s*\d+\s*$/;

/** Flex data is generated once per day by IBKR, so positions and cash are as of the last report. */
export const FLEX_FRESHNESS_NOTE =
  'IBKR publishes Flex reports once a day, so positions and cash are as of the last daily report — not live prices.';
