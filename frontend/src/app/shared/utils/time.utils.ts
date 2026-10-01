const MS_PER_SECOND = 1000;
const SECONDS_PER_MINUTE = 60;
const MS_PER_MINUTE = MS_PER_SECOND * SECONDS_PER_MINUTE;
const MINUTES_PER_HOUR = 60;
const HOURS_PER_DAY = 24;

export class TimeUtils {
  public static getRelativeTime(timestamp: Nullable<string>): string {
    if (!timestamp) {
      return 'Never';
    }
    const diffMs = Date.now() - new Date(timestamp).getTime();
    const minutes = Math.floor(diffMs / MS_PER_MINUTE);
    if (minutes < 1) {
      return 'Just now';
    }
    if (minutes < MINUTES_PER_HOUR) {
      return `${minutes}m ago`;
    }
    const hours = Math.floor(minutes / MINUTES_PER_HOUR);
    if (hours < HOURS_PER_DAY) {
      return `${hours}h ago`;
    }
    return `${Math.floor(hours / HOURS_PER_DAY)}d ago`;
  }
}
