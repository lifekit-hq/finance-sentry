const PAGE_SIZE_SMALL = 20;
const PAGE_SIZE_MEDIUM = 50;
const PAGE_SIZE_LARGE = 100;

export const ALERT_PAGE_SIZE_OPTIONS = [PAGE_SIZE_SMALL, PAGE_SIZE_MEDIUM, PAGE_SIZE_LARGE];

// A resolved alert reads as inactive through cmn-alert-item's read colours (title, body and
// timestamp at full opacity, all AA); only the leading severity icon fades, never the text.
export const RESOLVED_ALERT_ROW_CLASS = '[&_div>cmn-icon]:opacity-60';
