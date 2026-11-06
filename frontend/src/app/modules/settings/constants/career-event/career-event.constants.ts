import {type MenuItem, type SelectOption} from '@lifekit-hq/ui';

import {type CareerEventKind} from '../../models/career-event/career-event.model';

/** The label and note length caps. */
export const CAREER_EVENT_LABEL_MAX_LENGTH = 100;
export const CAREER_EVENT_NOTE_MAX_LENGTH = 500;
export const CAREER_EVENT_NOTE_ROWS = 2;

export const CAREER_EVENT_DEFAULT_CURRENCY = 'USD';

export const CAREER_EVENT_KIND_LABEL: ReadonlyMap<CareerEventKind, string> = new Map([
  ['IncomeStep', 'Income change'],
  ['OneOffIn', 'One-off in (bonus, payout)'],
  ['OneOffOut', 'One-off cost'],
  ['Leave', 'Leave (income drops for a period)'],
]);

/** The short name a row shows beside its date. */
export const CAREER_EVENT_KIND_SHORT_LABEL: ReadonlyMap<CareerEventKind, string> = new Map([
  ['IncomeStep', 'Income change'],
  ['OneOffIn', 'One-off in'],
  ['OneOffOut', 'One-off cost'],
  ['Leave', 'Leave'],
]);

export const CAREER_EVENT_KIND_OPTIONS: SelectOption[] = [...CAREER_EVENT_KIND_LABEL].map(
  ([value, label]) => ({value, label})
);

/** What the amount field means for each kind, so the person knows which sign to type. */
export const CAREER_EVENT_AMOUNT_HINT: ReadonlyMap<CareerEventKind, string> = new Map([
  ['IncomeStep', 'The change to your monthly income: negative for a pay cut. Stored encrypted.'],
  ['OneOffIn', 'The amount you receive. Stored encrypted.'],
  ['OneOffOut', 'The amount you spend. Stored encrypted.'],
  ['Leave', 'How much your income drops each month of the leave. Stored encrypted.'],
]);

export const CAREER_EVENT_MENU_ACTION_EDIT = 'edit';
export const CAREER_EVENT_MENU_ACTION_RETIRE = 'retire';
export const CAREER_EVENT_MENU_ACTION_DELETE = 'delete';

const EDIT_ITEM: MenuItem = {id: CAREER_EVENT_MENU_ACTION_EDIT, label: 'Edit', icon: 'Pencil'};
const RETIRE_ITEM: MenuItem = {
  id: CAREER_EVENT_MENU_ACTION_RETIRE,
  label: 'Retire',
  icon: 'Archive',
};
const DELETE_ITEM: MenuItem = {
  id: CAREER_EVENT_MENU_ACTION_DELETE,
  label: 'Delete',
  icon: 'Trash2',
  destructive: true,
};

/** A counted event can be retired; one that no longer counts can only be edited or deleted. */
export const CAREER_EVENT_COUNTED_MENU: MenuItem[] = [EDIT_ITEM, RETIRE_ITEM, DELETE_ITEM];
export const CAREER_EVENT_UNCOUNTED_MENU: MenuItem[] = [EDIT_ITEM, DELETE_ITEM];

export const CAREER_EVENT_SKELETON_ROWS = 2;

/** What the timeline says about itself under its heading. */
export const CAREER_TIMELINE_DISCLOSURE =
  'A scenario you entered, not a forecast. Each event changes what you save by its amount from its date; your spending is assumed not to move and tax is not modelled. A leave under way counts the months it still has ahead; any other event dated today or earlier is already part of your baseline and is not counted.';
