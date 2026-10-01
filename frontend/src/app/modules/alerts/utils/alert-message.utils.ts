const LONG_DECIMAL = /\d+\.\d{3,}/g;
const DISPLAY_DECIMALS = 2;

export class AlertMessageUtils {
  /** Rounds machine-precision decimals in server-written alert text (`1234.56789` → `1234.57`). */
  public static roundNumbers(message: Nullable<string>): string {
    if (!message) {
      return '';
    }
    return message.replace(LONG_DECIMAL, match => Number(match).toFixed(DISPLAY_DECIMALS));
  }
}
