import {type ProblemReportDevice} from '../models/report-problem.model';

/** Browser and OS family only, from the user agent; the API drops anything outside its known list anyway. */
export class ClientInfoUtils {
  /** Order matters: Edge, Opera and Samsung Internet all carry a `Chrome/` token, Chrome one for `Safari/`. */
  public static browser(userAgent: string): Nullable<string> {
    if (/\bEdgA?\/|\bEdgios?\//i.test(userAgent)) {
      return 'Edge';
    }
    if (/\bOPR\/|\bOpera\b/i.test(userAgent)) {
      return 'Opera';
    }
    if (/SamsungBrowser/i.test(userAgent)) {
      return 'Samsung Internet';
    }
    if (/Firefox\/|FxiOS\//i.test(userAgent)) {
      return 'Firefox';
    }
    if (/Chrome\/|CriOS\//i.test(userAgent)) {
      return 'Chrome';
    }
    return /Safari\//i.test(userAgent) ? 'Safari' : null;
  }

  public static os(userAgent: string): Nullable<string> {
    if (/iPad/i.test(userAgent)) {
      return 'iPadOS';
    }
    if (/iPhone|iPod/i.test(userAgent)) {
      return 'iOS';
    }
    if (/Android/i.test(userAgent)) {
      return 'Android';
    }
    if (/CrOS/i.test(userAgent)) {
      return 'ChromeOS';
    }
    if (/Windows/i.test(userAgent)) {
      return 'Windows';
    }
    if (/Mac OS X|Macintosh/i.test(userAgent)) {
      return 'macOS';
    }
    return /Linux/i.test(userAgent) ? 'Linux' : null;
  }

  public static device(isPhone: boolean): ProblemReportDevice {
    return isPhone ? 'Phone' : 'Desktop';
  }
}
