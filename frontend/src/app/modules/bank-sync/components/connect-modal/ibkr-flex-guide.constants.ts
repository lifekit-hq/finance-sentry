/** Sections the Activity Flex Query must include — mirrors what the Flex parser reads. */
export const FLEX_QUERY_SECTIONS: readonly string[] = [
  'Trades',
  'Cash Transactions',
  'Financial Instrument Information',
  'Open Positions',
  'Cash Report',
];

export interface FlexDeliverySetting {
  readonly label: string;
  readonly value: string;
}

/** Delivery Configuration values the Flex parser expects (dates and times parse as yyyyMMdd;HHmmss). */
export const FLEX_DELIVERY_SETTINGS: readonly FlexDeliverySetting[] = [
  {label: 'Format', value: 'XML'},
  {label: 'Period', value: 'Last 365 Calendar Days'},
  {label: 'Date Format', value: 'yyyyMMdd'},
  {label: 'Time Format', value: 'HHmmss'},
  {label: 'Date/Time Separator', value: '; (semi-colon)'},
  {label: 'Include Canceled Trades', value: 'No'},
  {label: 'Display Account Alias in Place of Account ID', value: 'No'},
  {label: 'Breakout by Day', value: 'No'},
];

export interface OauthGuideStep {
  readonly title: string;
  readonly body: string;
}

/** Short guide for the optional live-prices OAuth path. */
export const OAUTH_GUIDE_STEPS: readonly OauthGuideStep[] = [
  {
    title: 'Register a consumer',
    body: "Open IBKR's self-service OAuth portal and register a consumer. You choose a 9-character consumer key.",
  },
  {
    title: 'Generate the three key files',
    body: 'On your computer run: openssl genrsa -out private_signature.pem 2048, then the same for private_encryption.pem, then openssl dhparam -out dhparam.pem 2048.',
  },
  {
    title: 'Upload the public halves',
    body: 'Export the public key of each RSA file and upload both, plus dhparam.pem, in the portal. It then shows an access token and an access token secret (the secret is shown once).',
  },
  {
    title: 'Paste everything here',
    body: 'Enter the consumer key, access token and secret, and pick the three .pem files. IBKR can take a day to activate a new consumer key.',
  },
];
