import {PUSH_DEVICE_STORAGE_KEY} from '../constants/push/push.constants';
import {type RegisterPushRequest} from '../models/push/push.model';

export class PushDeviceUtils {
  /** The id of this browser's registered subscription; null when none or storage is unavailable. */
  public static readId(): Nullable<string> {
    try {
      return localStorage.getItem(PUSH_DEVICE_STORAGE_KEY);
    } catch {
      return null;
    }
  }

  public static writeId(id: string): void {
    try {
      localStorage.setItem(PUSH_DEVICE_STORAGE_KEY, id);
    } catch {
      // Storage blocked: the device list still works, only "this device" is not recognised.
    }
  }

  public static clearId(): void {
    try {
      localStorage.removeItem(PUSH_DEVICE_STORAGE_KEY);
    } catch {
      // Nothing was stored.
    }
  }

  /** Maps the browser's subscription JSON to the API body; throws when the browser omitted a part of it. */
  public static toRequest(json: PushSubscriptionJSON): RegisterPushRequest {
    const p256dh = json.keys?.['p256dh'];
    const auth = json.keys?.['auth'];
    if (!json.endpoint || !p256dh || !auth) {
      throw new Error('Incomplete push subscription');
    }
    return {endpoint: json.endpoint, keys: {p256dh, auth}};
  }
}
