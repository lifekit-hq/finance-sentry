import {type Nullable} from '@lifekit-hq/ui';

export type IncomeKind = 'Salary' | 'Side' | 'Other';

export type IncomeCadence = 'Monthly' | 'Weekly' | 'Fortnightly' | 'Annual' | 'Irregular';

/** Whether the declared amount is before or after tax. */
export type IncomeBasis = 'Gross' | 'Net';

/** A declared income source (`GET /wealth/income-sources`); the amount is in its own currency. */
export interface IncomeSource {
  id: string;
  kind: IncomeKind;
  label: string;
  amount: number;
  currency: string;
  cadence: IncomeCadence;
  grossOrNet: IncomeBasis;
  /** `yyyy-MM-dd`. */
  startDate: string;
  /** `yyyy-MM-dd`; null while the source is running. */
  endDate: Nullable<string>;
  /** The amount per month in `currency`; null when the cadence is irregular. */
  monthlyEquivalent: Nullable<number>;
  active: boolean;
  updatedAt: string;
}

/** The body `POST` / `PUT /wealth/income-sources` take. */
export interface IncomeSourceInput {
  kind: IncomeKind;
  label: string;
  amount: number;
  currency: string;
  cadence: IncomeCadence;
  grossOrNet: IncomeBasis;
  startDate: string;
  endDate: Nullable<string>;
}

/** What the add/edit dialog opens with: the source to edit, or none to add one. */
export interface IncomeSourceDialogData {
  source: Nullable<IncomeSource>;
}

/** A row of the Money profile list: the source plus what the section derives for display. */
export interface IncomeSourceRow extends IncomeSource {
  sublabel: string;
}
