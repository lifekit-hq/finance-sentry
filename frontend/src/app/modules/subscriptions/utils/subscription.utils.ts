import {type Subscription} from '../models/subscription/subscription.model';

const DEGREES = 360;
const SATURATION = 55;
const LIGHTNESS = 42;
const HASH_SHIFT = 5;
const MS_PER_DAY = 86_400_000;

export class SubscriptionUtils {
  public static daysUntil(dateStr: string, now: number = Date.now()): number {
    return Math.ceil((new Date(dateStr).getTime() - now) / MS_PER_DAY);
  }

  public static getMerchantColor(name: string): string {
    if (!name) {
      return 'hsl(220, 14%, 50%)';
    }
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << HASH_SHIFT) - hash);
    }
    const hue = ((hash % DEGREES) + DEGREES) % DEGREES;
    return `hsl(${hue}, ${SATURATION}%, ${LIGHTNESS}%)`;
  }

  public static installmentProgress(
    item: Pick<Subscription, 'occurrenceCount' | 'termCount' | 'remainingPayments'>
  ): string {
    if (item.termCount) {
      const left = item.remainingPayments === null ? '' : ` · ${item.remainingPayments} left`;
      return `${item.occurrenceCount} / ${item.termCount} paid${left}`;
    }
    return `${item.occurrenceCount} payment${item.occurrenceCount === 1 ? '' : 's'}`;
  }
}
