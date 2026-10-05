import {type Alert} from '../models/alert/alert.model';

export class AlertStatusUtils {
  /** Small caption under an alert's title: "Resolved", "×4", or "Resolved · ×4"; null when neither applies. */
  public static caption(alert: Pick<Alert, 'isResolved' | 'occurrenceCount'>): Nullable<string> {
    const parts: string[] = [];
    if (alert.isResolved) {
      parts.push('Resolved');
    }
    if (alert.occurrenceCount > 1) {
      parts.push(`×${alert.occurrenceCount}`);
    }
    return parts.length > 0 ? parts.join(' · ') : null;
  }
}
