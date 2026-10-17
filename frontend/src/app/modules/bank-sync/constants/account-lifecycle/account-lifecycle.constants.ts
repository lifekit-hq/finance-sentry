/** Why the closed row says an account closed, by the backend's `AccountClosedReasons` value. */
export const CLOSED_REASON_REISSUED = 'reissued';
export const CLOSED_REASON_ABSENT = 'absent_from_list';

export const CARD_EXPIRED_NOTE = 'Card expired - your bank usually sends a new one';
export const CLOSED_REASON_BY_YOU = 'Closed by you';
export const CLOSED_REASON_REISSUED_LABEL = 'Replaced by a new card';

/** `valid_to` arrives as a year-month; a trailing day (`2027-01-31`) is ignored. */
export const CARD_VALID_TO_PATTERN = /^(\d{4})-(\d{2})/;
export const MONTHS_PER_YEAR = 12;
