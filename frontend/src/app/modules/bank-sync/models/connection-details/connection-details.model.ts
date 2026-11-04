/** One labelled line of the connection details sheet, already worded. */
export interface ConnectionDetailRow {
  label: string;
  value: string;
}

export interface ConnectionDetailsDialogData {
  rows: ConnectionDetailRow[];
}
