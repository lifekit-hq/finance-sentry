/** How freely an entered value can be spent (finance-framework §5.2): only `Cash` counts as cash. */
export type Convertibility = 'Cash' | 'InEconomy' | 'Locked';

/** A value the person enters by hand (`GET /wealth/manual-holdings`), e.g. a Steam wallet after fees. */
export interface ManualHolding {
  id: string;
  label: string;
  amount: number;
  currency: string;
  /** `yyyy-MM-dd` the value was true on. */
  asOf: string;
  convertibility: Convertibility;
  reliability: string;
  updatedAt: string;
}

/** The body `POST` / `PUT /wealth/manual-holdings` take. */
export interface ManualHoldingInput {
  label: string;
  amount: number;
  currency: string;
  asOf: string;
  convertibility: Convertibility;
}

/** What the add/edit dialog opens with: the entry to edit, or none to add one. */
export interface ManualHoldingDialogData {
  holding: Nullable<ManualHolding>;
}

/** A row of the "Entered by hand" section. */
export interface ManualHoldingRow extends ManualHolding {
  sublabel: string;
}
