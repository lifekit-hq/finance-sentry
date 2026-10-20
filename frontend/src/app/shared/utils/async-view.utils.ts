import {formatDate} from '@angular/common';
import {type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../constants/date/date.constants';
import {type AsyncView, type AsyncViewInput} from '../models/async-view/async-view.model';

const SYNC_TIME_FORMAT = 'HH:mm';
const SYNC_DATE_TIME_FORMAT = 'd MMM, HH:mm';

export const OFFLINE_ERROR_MESSAGE = "You're offline. Reconnect to load this page.";

export class AsyncViewUtils {
  /**
   * The one place a screen decides which state to show: a skeleton only while nothing is on
   * screen yet (a retry keeps the stale data), an error never reads as empty, and offline keeps
   * the last data with a "last synced" notice instead of an error.
   */
  public static resolve(input: AsyncViewInput, now: Date = new Date()): AsyncView {
    if (input.offline && input.hasData) {
      return {
        status: 'success',
        errorMessage: '',
        offlineNotice: AsyncViewUtils.offlineNotice(input.lastSyncedAt, now),
      };
    }
    if (input.isLoading && (!input.hasData || input.keepContent === true)) {
      return {status: 'loading', errorMessage: '', offlineNotice: null};
    }
    if (input.errorMessage !== '' && !input.isLoading) {
      return {
        status: 'error',
        errorMessage: input.offline ? OFFLINE_ERROR_MESSAGE : input.errorMessage,
        offlineNotice: null,
      };
    }
    return {status: 'success', errorMessage: '', offlineNotice: null};
  }

  public static lastSyncedLabel(lastSyncedAt: Nullable<number>, now: Date = new Date()): string {
    if (lastSyncedAt === null) {
      return '';
    }
    const at = new Date(lastSyncedAt);
    const sameDay = at.toDateString() === now.toDateString();
    return formatDate(at, sameDay ? SYNC_TIME_FORMAT : SYNC_DATE_TIME_FORMAT, APP_LOCALE);
  }

  public static offlineNotice(lastSyncedAt: Nullable<number>, now: Date = new Date()): string {
    const label = AsyncViewUtils.lastSyncedLabel(lastSyncedAt, now);
    return label === ''
      ? "You're offline. Showing the data from your last sync."
      : `You're offline. Showing the data from your last sync (${label}).`;
  }
}
