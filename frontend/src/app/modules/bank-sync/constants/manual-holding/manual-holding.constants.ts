import {type MenuItem, type SelectOption} from '@lifekit-hq/ui';

import {type Convertibility} from '../../models/manual-holding/manual-holding.model';

/** Mirrors the backend's `ManualHoldingLimits.LabelMaxLength`. */
export const MANUAL_HOLDING_LABEL_MAX_LENGTH = 100;

export const MANUAL_HOLDING_MIN_AMOUNT = 0.01;

export const MANUAL_HOLDING_DEFAULT_CURRENCY = 'USD';

/** Short words for a row's sublabel. */
export const CONVERTIBILITY_LABEL: ReadonlyMap<Convertibility, string> = new Map([
  ['Cash', 'Spendable anywhere'],
  ['InEconomy', 'Spendable only inside its platform'],
  ['Locked', 'Locked for now'],
]);

export const CONVERTIBILITY_OPTIONS: SelectOption[] = [
  {value: 'InEconomy', label: 'Only inside its platform, like Steam'},
  {value: 'Cash', label: 'Anywhere, like cash'},
  {value: 'Locked', label: 'Not until a later date'},
];

export const MENU_ACTION_EDIT = 'edit';
export const MENU_ACTION_REMOVE_MANUAL = 'remove';

export const MANUAL_HOLDING_MENU: MenuItem[] = [
  {id: MENU_ACTION_EDIT, label: 'Edit', icon: 'Pencil'},
  {id: MENU_ACTION_REMOVE_MANUAL, label: 'Remove', icon: 'Trash2', destructive: true},
];
