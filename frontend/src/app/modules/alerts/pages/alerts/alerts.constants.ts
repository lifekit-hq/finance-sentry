import {type CmnRowSwipeAction} from '@lifekit-hq/ui';

// A resolved alert reads as inactive through cmn-alert-item's read colours (title, body and
// timestamp at full opacity, all AA); only the leading severity icon fades, never the text.
export const RESOLVED_ALERT_ROW_CLASS = '[&_div>cmn-icon]:opacity-60';

// A swipe toward the start reveals Dismiss (Apple HIG: the destructive action on the trailing
// edge). The Dismiss X stays on the row; the swipe is a shortcut to it.
export const DISMISS_SWIPE_ACTION: CmnRowSwipeAction = {
  label: 'Dismiss',
  icon: 'X',
  tone: 'danger',
};
