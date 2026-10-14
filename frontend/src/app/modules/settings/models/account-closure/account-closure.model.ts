export interface CloseAccountDialogData {
  /** The signed-in account's email; the user types it to confirm. */
  readonly email: string;
  /** Days until the closed account's data is purged unless an owner reopens it. */
  readonly purgeDays: number;
}

export interface CloseAccountRequest {
  readonly confirmEmail: string;
}

/** Whether the signed-in person may close their account now, and why not when they may not. */
export interface AccountClosureOptions {
  readonly canClose: boolean;
  readonly reason: Nullable<string>;
  readonly graceDays: number;
}

/** A closed account: when it was closed and when its data is erased. */
export interface AccountClosure {
  readonly closedAt: string;
  readonly purgeDueAt: string;
}
