import {type DisposalTaxRegime} from '../../models/dossier/dossier.model';

/** Why an estimate is withheld, by the backend's reason code: the data cannot support a figure. */
export const DISPOSAL_TAX_REASON_LABEL: Readonly<Record<string, string>> = {
  PROVIDER_NOT_SUPPORTED: 'Only positions held at Interactive Brokers are estimated.',
  PARTIAL_COVERAGE: 'The imported trades do not explain the whole holding.',
  UNCLASSIFIED: 'This instrument has no tax classification (ordinary share or fund).',
  BASIS_NOT_VERIFIED: 'The cost basis is not verified against the imported trades.',
  FOUR_WEEK_RULE:
    'A buy and a sell within four weeks of each other fall under Irish matching rules, which are not modelled.',
  DEEMED_DISPOSAL_PASSED:
    'A lot has passed its 8-year deemed disposal, and its value on that day is not imported.',
};

export const DISPOSAL_TAX_REASON_FALLBACK = 'The data does not support an estimate.';

export const DISPOSAL_TAX_REGIME_LABEL: Readonly<Record<DisposalTaxRegime, string>> = {
  cgt: 'Capital gains tax',
  exitTax: 'Exit tax',
};

export const DISPOSAL_TAX_WITHHELD_TITLE = 'Not enough data to estimate';
export const DISPOSAL_TAX_EXEMPTION_NOTE =
  'before the annual exemption, which all of the year’s disposals share';
export const DISPOSAL_TAX_EXIT_NOTE =
  'Exit tax is also charged on each fund unit’s gain every 8 years, whether or not it is sold.';
export const DISPOSAL_TAX_CARRIED_FORWARD_NOTE =
  'No rates have been reviewed for this year yet; the latest reviewed year’s are shown.';
export const PERCENT_SCALE = 100;
export const PERCENT_DECIMALS = 2;
