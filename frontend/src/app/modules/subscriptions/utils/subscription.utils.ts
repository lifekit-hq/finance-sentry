import {type Subscription} from '../models/subscription/subscription.model';

const DEGREES = 360;
const SATURATION = 55;
const LIGHTNESS = 42;
const HASH_SHIFT = 5;
const MS_PER_DAY = 86_400_000;
// Detection builds this label itself (`Mobile top-up 0057`); the statement line reads `*MOBI TOP-UP 0857860057`.
const SYNTHESIZED_NAME = /^mobile top-up \d{4}$/i;

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

  /**
   * The ledger search that lands on a row's charges, or null when the row has no such text. A
   * detected row is named after a raw statement merchant or description, which the ledger search
   * matches; a hand-typed label or a name detection composes appears on no statement.
   */
  public static chargesQuery(
    item: Pick<Subscription, 'isManual' | 'merchantName'>
  ): Nullable<string> {
    const name = item.merchantName.trim();
    return item.isManual || !name || SYNTHESIZED_NAME.test(name) ? null : name;
  }
}
