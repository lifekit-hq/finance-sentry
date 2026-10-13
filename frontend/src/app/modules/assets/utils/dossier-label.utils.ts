import {
  ANALYST_ACTION_LABELS,
  COVERAGE_LABELS,
  DATA_SOURCE_PREFIX_LABELS,
  NEWS_SOURCE_LABELS,
  NEWS_SOURCE_PREFIX_LABELS,
  PEER_SET_DEFAULT_SUFFIX,
  PEER_SET_LABELS,
  PEER_SET_SECTOR_PREFIX,
  SIGNAL_TYPE_LABELS,
} from '../constants/dossier/dossier-labels.constants';

export class DossierLabelUtils {
  public static coverage(code: string): string {
    return COVERAGE_LABELS[code] ?? DossierLabelUtils.humanize(code);
  }

  public static signalType(code: string): string {
    return SIGNAL_TYPE_LABELS[code] ?? DossierLabelUtils.humanize(code);
  }

  /** `TargetChange` -> "Target changed" (any casing); an action the map does not know is title-cased. */
  public static analystAction(code: string): string {
    const key = Object.keys(ANALYST_ACTION_LABELS).find(
      k => k.toLowerCase() === code.toLowerCase()
    );
    return key ? ANALYST_ACTION_LABELS[key] : DossierLabelUtils.humanize(code);
  }

  /**
   * The provider's name for a news feed label: `yahoo:GRAB` -> "Yahoo Finance". A source that is
   * already a publisher's name ("Reuters") passes through untouched, never title-cased.
   */
  public static newsSource(source: string): string {
    const trimmed = source.trim();
    const known = NEWS_SOURCE_LABELS[trimmed];
    if (known) {
      return known;
    }
    const prefix = Object.keys(NEWS_SOURCE_PREFIX_LABELS).find(p => trimmed.startsWith(p));
    return prefix ? NEWS_SOURCE_PREFIX_LABELS[prefix] : trimmed;
  }

  /** A provenance code as a provider name (`yahoo-finance` -> "Yahoo Finance"); an unknown code is title-cased. */
  public static dataSource(code: string): string {
    const trimmed = code.trim().toLowerCase();
    const prefix = Object.keys(DATA_SOURCE_PREFIX_LABELS).find(p => trimmed.startsWith(p));
    return prefix ? DATA_SOURCE_PREFIX_LABELS[prefix] : DossierLabelUtils.humanize(code);
  }

  /** `sector:Technology (default)` -> "Technology peers"; `default` -> "Similar companies". */
  public static peerSet(name: string): string {
    const known = PEER_SET_LABELS[name];
    if (known) {
      return known;
    }
    if (name.startsWith(PEER_SET_SECTOR_PREFIX)) {
      const sector = name
        .slice(PEER_SET_SECTOR_PREFIX.length)
        .replace(PEER_SET_DEFAULT_SUFFIX, '')
        .trim();
      return sector ? `${sector} peers` : PEER_SET_LABELS['default'];
    }
    return name;
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
