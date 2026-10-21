import {type Nullable} from '@lifekit-hq/ui';

import {type Alert} from '../../models/alert/alert.model';

export interface TopAlertState {
  /** The newest unread alert; null when everything is read. */
  alert: Nullable<Alert>;
  unreadCount: number;
}

export const initialTopAlertState: TopAlertState = {
  alert: null,
  unreadCount: 0,
};
