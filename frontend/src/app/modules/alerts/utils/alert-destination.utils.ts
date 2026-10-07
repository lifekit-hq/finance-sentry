import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type Alert} from '../models/alert/alert.model';
import {type AlertNavigation} from '../models/alert/alert-destination.model';
import {AlertMessageUtils} from './alert-message.utils';
import {AlertTypeUtils} from './alert-type.utils';

// Dossier alerts carry the ticker as their reference label; a non-ticker label (the radar
// feed-freshness alert says "freshness") has no dossier to open.
const TICKER = /^[A-Z0-9][A-Z0-9.-]{0,14}$/;

export class AlertDestinationUtils {
  /** Where tapping the alert goes: the server-resolved appPath, else the type's coarse destination;, or null when the alert only gets marked read. */
  public static resolve(alert: Alert): Nullable<AlertNavigation> {
    if (alert.type === 'FilingLanded') {
      const url = AlertMessageUtils.filingUrl(alert.message);
      if (url) {
        return {kind: 'external', url};
      }
    }

    const appPath = alert.appPath?.trim();
    if (appPath && appPath.startsWith('/') && !appPath.startsWith('//')) {
      return {kind: 'url', url: appPath};
    }

    const destination = AlertTypeUtils.meta(alert.type).destination;
    if (destination === 'none') {
      return null;
    }
    if (destination === 'dossier') {
      const symbol = alert.referenceLabel?.trim() ?? '';
      return TICKER.test(symbol)
        ? {kind: 'route', commands: [AppRoute.AssetDossier, symbol]}
        : null;
    }
    return {kind: 'route', commands: [destination]};
  }
}
