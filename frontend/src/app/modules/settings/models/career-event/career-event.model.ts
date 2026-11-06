import {type Nullable} from '@lifekit-hq/ui';

/**
 * What a dated career-money event does to the monthly saving. An income step is a signed monthly change, a leave a
 * positive monthly reduction through its end date, a one-off a positive amount on its date.
 */
export type CareerEventKind = 'IncomeStep' | 'OneOffIn' | 'OneOffOut' | 'Leave';

/** An entry of the career timeline (`GET /wealth/career-events`); the amount is in its own currency. */
export interface CareerEvent {
  id: string;
  kind: CareerEventKind;
  /** `yyyy-MM-dd`. */
  date: string;
  /** `yyyy-MM-dd`; set for a leave only. */
  endDate: Nullable<string>;
  label: string;
  amount: number;
  currency: string;
  note: Nullable<string>;
  /** Whether the scenario applies it: not retired and dated after today. */
  counted: boolean;
  retired: boolean;
  updatedAt: string;
}

/** The body `POST` / `PUT /wealth/career-events` take. */
export interface CareerEventInput {
  kind: CareerEventKind;
  date: string;
  endDate: Nullable<string>;
  label: string;
  amount: number;
  currency: string;
  note: Nullable<string>;
}

/** What the add/edit dialog opens with: the event to edit, or none to add one. */
export interface CareerEventDialogData {
  event: Nullable<CareerEvent>;
}

/** A row of the timeline: the event plus what the section derives for display. */
export interface CareerEventRow extends CareerEvent {
  sublabel: string;
  /** The amount with the sign it has on the saving: negative for a cost or a pay cut. */
  signedAmount: number;
  /** Whether `signedAmount` repeats every month (an income step, a leave) or happens once. */
  monthly: boolean;
}
