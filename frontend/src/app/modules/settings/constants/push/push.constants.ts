/** localStorage key holding the id of this browser's registered push subscription. */
export const PUSH_DEVICE_STORAGE_KEY = 'fns.push.deviceId';

/** Frontend-only error codes (the browser, not the API, refused); messages live in the error registry. */
export const PUSH_PERMISSION_DENIED = 'PUSH_PERMISSION_DENIED';
export const PUSH_SUBSCRIBE_FAILED = 'PUSH_SUBSCRIBE_FAILED';
