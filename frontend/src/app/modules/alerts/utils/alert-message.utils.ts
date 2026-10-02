const LONG_DECIMAL = /\d+\.\d{3,}/g;
const DISPLAY_DECIMALS = 2;
const TRAILING_SEC_URL = /\s*(https:\/\/(?:[\w-]+\.)*sec\.gov\/\S*)\s*$/;

export class AlertMessageUtils {
  /** Rounds machine-precision decimals in server-written alert text (`1234.56789` → `1234.57`). */
  public static roundNumbers(message: Nullable<string>): string {
    if (!message) {
      return '';
    }
    return message.replace(LONG_DECIMAL, match => Number(match).toFixed(DISPLAY_DECIMALS));
  }

  /** Message text without a trailing sec.gov URL (the URL is exposed by `filingUrl`). */
  public static stripFilingUrl(message: Nullable<string>): string {
    return message ? message.replace(TRAILING_SEC_URL, '') : '';
  }

  /** The trailing sec.gov URL of a filing alert message, or null when there is none. */
  public static filingUrl(message: Nullable<string>): Nullable<string> {
    return message?.match(TRAILING_SEC_URL)?.[1] ?? null;
  }
}
