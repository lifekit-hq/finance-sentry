/** `GET notifications/push/public-key`: whether the server can send push, and the VAPID key to subscribe with. */
export interface PushPublicKey {
  available: boolean;
  publicKey: Nullable<string>;
}

/** One registered browser or device. `disabled` is set after the push service repeatedly refused it. */
export interface PushDevice {
  id: string;
  deviceLabel: Nullable<string>;
  createdAt: string;
  lastSuccessAt: Nullable<string>;
  disabled: boolean;
}

/** A device row plus what the page derives for display. */
export interface PushDeviceRow extends PushDevice {
  isThisDevice: boolean;
}

export interface PushPreferences {
  pushEnabled: boolean;
}

/** The browser's `PushSubscription.toJSON()` shape, as the API expects it. */
export interface RegisterPushRequest {
  endpoint: string;
  keys: {p256dh: string; auth: string};
}
