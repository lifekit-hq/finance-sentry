import {type MenuItem, type SelectOption} from '@lifekit-hq/ui';

import {
  type IncomeBasis,
  type IncomeCadence,
  type IncomeKind,
} from '../../models/income-source/income-source.model';

/** The income label's length cap. */
export const INCOME_SOURCE_LABEL_MAX_LENGTH = 100;

export const INCOME_SOURCE_MIN_AMOUNT = 0.01;

export const INCOME_SOURCE_DEFAULT_CURRENCY = 'USD';

export const INCOME_KIND_LABEL: ReadonlyMap<IncomeKind, string> = new Map([
  ['Salary', 'Salary'],
  ['Side', 'Side income'],
  ['Other', 'Other'],
]);

export const INCOME_CADENCE_LABEL: ReadonlyMap<IncomeCadence, string> = new Map([
  ['Monthly', 'Monthly'],
  ['Weekly', 'Weekly'],
  ['Fortnightly', 'Fortnightly'],
  ['Annual', 'Annual'],
  ['Irregular', 'Irregular'],
]);

export const INCOME_BASIS_LABEL: ReadonlyMap<IncomeBasis, string> = new Map([
  ['Gross', 'Before tax'],
  ['Net', 'After tax'],
]);

export const INCOME_KIND_OPTIONS: SelectOption[] = [...INCOME_KIND_LABEL].map(([value, label]) => ({
  value,
  label,
}));

export const INCOME_CADENCE_OPTIONS: SelectOption[] = [...INCOME_CADENCE_LABEL].map(
  ([value, label]) => ({value, label})
);

export const INCOME_BASIS_OPTIONS: SelectOption[] = [...INCOME_BASIS_LABEL].map(
  ([value, label]) => ({value, label})
);

export const INCOME_MENU_ACTION_EDIT = 'edit';
export const INCOME_MENU_ACTION_RETIRE = 'retire';
export const INCOME_MENU_ACTION_DELETE = 'delete';

const EDIT_ITEM: MenuItem = {id: INCOME_MENU_ACTION_EDIT, label: 'Edit', icon: 'Pencil'};
const RETIRE_ITEM: MenuItem = {id: INCOME_MENU_ACTION_RETIRE, label: 'Retire', icon: 'Archive'};
const DELETE_ITEM: MenuItem = {
  id: INCOME_MENU_ACTION_DELETE,
  label: 'Delete',
  icon: 'Trash2',
  destructive: true,
};

/** A running source can be retired; a retired one can only be edited or deleted. */
export const INCOME_ACTIVE_MENU: MenuItem[] = [EDIT_ITEM, RETIRE_ITEM, DELETE_ITEM];
export const INCOME_RETIRED_MENU: MenuItem[] = [EDIT_ITEM, DELETE_ITEM];

export const INCOME_SKELETON_ROWS = 2;
