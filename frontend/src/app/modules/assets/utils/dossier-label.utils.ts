import {COVERAGE_LABELS, SIGNAL_TYPE_LABELS} from '../constants/dossier/dossier-labels.constants';

export class DossierLabelUtils {
  public static coverage(code: string): string {
    return COVERAGE_LABELS[code] ?? DossierLabelUtils.humanize(code);
  }

  public static signalType(code: string): string {
    return SIGNAL_TYPE_LABELS[code] ?? DossierLabelUtils.humanize(code);
  }

  /** Title-case fallback for an unknown code: "SOME_NEW_code" / "someNewCode" -> "Some New Code". */
  public static humanize(code: string): string {
    return code
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .split(/[\s_-]+/)
      .filter(Boolean)
      .map(word => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
      .join(' ');
  }
}
