import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type Alert} from '../models/alert/alert.model';
import {type AlertNavigation} from '../models/alert/alert-destination.model';
import {AlertMessageUtils} from './alert-message.utils';
import {AlertTypeUtils} from './alert-type.utils';

// Dossier alerts carry the ticker as their reference label; a non-ticker label (the radar
// feed-freshness alert says "freshness") has no dossier to open.
const TICKER = /^[A-Z0-9][A-Z0-9.-]{0,14}$/;

/** The alert fields a destination is derived from; a fired event carries the first three. */
export type AlertDestinationSource = Pick<Alert, 'type' | 'message' | 'referenceLabel' | 'appPath'>;

export class AlertDestinationUtils {
  /** Where tapping the alert goes: the server-resolved appPath, else the type's coarse destination, or null when the alert only gets marked read. */
  public static resolve(alert: AlertDestinationSource): Nullable<AlertNavigation> {
    if (alert.type === 'FilingLanded') {
      const url = AlertMessageUtils.filingUrl(alert.message);
      if (url) {
        return {kind: 'external', url};
      }
    }

    // The server keeps merchant names out of the path it stores (it rides on push payloads), so a
    // duplicate charge narrows to its merchant's charges here, from the statement name its message
    // quotes (its reference label is a normalized key the ledger search does not match).
    if (alert.type === 'DuplicateCharge') {
      const merchant = AlertMessageUtils.duplicateChargeMerchant(alert.message);
      if (merchant) {
        return {kind: 'url', url: `${AppRoute.Transactions}?q=${encodeURIComponent(merchant)}`};
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
      return AlertDestinationUtils.dossier(alert.referenceLabel);
    }
    return {kind: 'route', commands: [destination]};
  }

  /** The asset dossier of a ticker label, or null when the label is not a ticker. */
  public static dossier(label: Nullable<string>): Nullable<AlertNavigation> {
    const symbol = label?.trim() ?? '';
    return TICKER.test(symbol) ? {kind: 'route', commands: [AppRoute.AssetDossier, symbol]} : null;
  }
}
