import {DateUtils} from '../../../shared/utils/date.utils';
import {type Alert, type AlertDayGroup} from '../models/alert/alert.model';

export class AlertDayUtils {
  /**
   * Groups a newest-first page into the viewer's calendar days, keeping the incoming order. An
   * unparseable `createdAt` gets a group of its own keyed by the raw text.
   */
  public static groupByDay(alerts: readonly Alert[], now: Date = new Date()): AlertDayGroup[] {
    const groups: AlertDayGroup[] = [];
    const byKey = new Map<string, AlertDayGroup>();
    for (const item of alerts) {
      const dayKey = AlertDayUtils.dayKey(item.createdAt);
      let group = byKey.get(dayKey);
      if (!group) {
        group = {dayKey, label: DateUtils.recentDayLabel(dayKey, now), items: []};
        byKey.set(dayKey, group);
        groups.push(group);
      }
      group.items.push(item);
    }
    return groups;
  }

  /** `yyyy-MM-dd` of the instant in the viewer's time zone; the raw text when it is not a date. */
  public static dayKey(createdAt: string): string {
    const instant = new Date(createdAt);
    return Number.isNaN(instant.getTime()) ? createdAt : DateUtils.isoDateLocal(instant);
  }
}
