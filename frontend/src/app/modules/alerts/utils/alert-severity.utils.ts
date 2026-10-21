import {type AlertItemSeverity} from '@lifekit-hq/ui';

import {type AlertSeverity} from '../models/alert/alert.model';

export class AlertSeverityUtils {
  /** The alert row's severity for an alert's: the API's names in the library's spelling. */
  public static toItemSeverity(severity: AlertSeverity): AlertItemSeverity {
    switch (severity) {
      case 'Error':
        return 'error';
      case 'Warning':
        return 'warning';
      case 'Info':
        return 'info';
    }
  }
}
