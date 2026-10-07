import {type AlertNavigation} from '../../alerts/models/alert/alert-destination.model';
import {AlertDestinationUtils} from '../../alerts/utils/alert-destination.utils';
import {type FiredEvent, type UpcomingEvent} from '../models/event/event.model';

export class EventDestinationUtils {
  /** The asset dossier of a calendar row's ticker; macro rows have nothing to open. */
  public static upcoming(
    event: Pick<UpcomingEvent, 'kind' | 'subject'>
  ): Nullable<AlertNavigation> {
    return event.kind === 'macro' ? null : AlertDestinationUtils.dossier(event.subject);
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
