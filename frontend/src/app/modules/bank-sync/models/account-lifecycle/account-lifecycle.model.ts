import {type Nullable} from '@lifekit-hq/ui';
/** One retired account in the "Closed accounts" section, every line already worded. */
export interface ClosedAccountRow {
  accountId: string;
  bankName: string;
  /** "Black · UAH": the card type (or account type) and the currency. */
  kindLabel: string;
  last4: string;
  closedLabel: string;
  reasonLabel: string;
  /** The balance the account held when it closed, not a live figure. */
  lastBalance: number;
  currency: string;
  /** False for a card replaced by a live successor: reopening it would count the same money twice. */
  reopenable: boolean;
}

/** The quiet lines under a live account row; each is null when it does not apply. */
export interface AccountNote {
  /** "Card expires 01/2027", or the expired wording once the month has passed. */
  expiry: Nullable<string>;
  /** "New card •••• 1234 (replaced •••• 9312)" on the account that took over a retired card. */
  replacement: Nullable<string>;
}
