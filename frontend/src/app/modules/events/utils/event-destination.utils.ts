import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type AlertNavigation} from '../../alerts/models/alert/alert-destination.model';
import {AlertDestinationUtils} from '../../alerts/utils/alert-destination.utils';
import {type FiredEvent, type UpcomingEvent} from '../models/event/event.model';

// Calendar subjects are tickers for every kind but macro (a release name such as "US CPI").
const TICKER = /^[A-Z0-9][A-Z0-9.-]{0,14}$/;

export class EventDestinationUtils {
  /** The asset dossier of a calendar row's ticker; macro rows have nothing to open. */
  public static upcoming(
    event: Pick<UpcomingEvent, 'kind' | 'subject'>
  ): Nullable<AlertNavigation> {
    const symbol = event.subject.trim();
    if (event.kind === 'macro' || !TICKER.test(symbol)) {
      return null;
    }
    return {kind: 'route', commands: [AppRoute.AssetDossier, symbol]};
  }

  /** What a fired event is about, resolved like the alert behind it. */
  public static fired(
    event: Pick<FiredEvent, 'kind' | 'message' | 'subject'>
  ): Nullable<AlertNavigation> {
    return AlertDestinationUtils.resolve({
      type: event.kind,
      message: event.message,
      referenceLabel: event.subject,
    });
  }
}
