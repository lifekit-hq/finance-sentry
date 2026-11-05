import {type SelectOption} from '@lifekit-hq/ui';

/** What a base-currency total is shown in until the API names the profile's base currency. */
export const DEFAULT_BASE_CURRENCY = 'USD';

/** The currencies the backend has a rate for; an unknown one is refused rather than counted 1:1. */
export const ENTRY_CURRENCY_OPTIONS: SelectOption[] = [
  {value: 'USD', label: 'USD — US Dollar'},
  {value: 'EUR', label: 'EUR — Euro'},
  {value: 'GBP', label: 'GBP — British Pound'},
  {value: 'UAH', label: 'UAH — Ukrainian Hryvnia'},
];
