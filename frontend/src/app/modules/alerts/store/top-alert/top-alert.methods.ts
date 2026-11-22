import {patchState, type WritableStateSource} from '@ngrx/signals';
import {type Alert} from '@shared/models/alert/alert.model';

import {type TopAlertState} from './top-alert.state';

export function topAlertMethods(store: WritableStateSource<TopAlertState>) {
  return {
    setTopAlert(alert: Alert | undefined, unreadCount: number): void {
      patchState(store, {alert: alert ?? null, unreadCount});
    },
  };
}
