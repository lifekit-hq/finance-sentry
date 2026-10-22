const SEPARATOR = ': ';

export class EventTitleUtils {
  /**
   * Title text for an event row once the kind tag and subject are already shown: drops a leading
   * "<kindLabel>: " and returns '' when what remains is just the subject. A detector's own wording
   * ("Unusual move: USDT-USD" under a "Market move" tag) is dropped the same way, so a title is
   * kept only when it carries something the tag and the subject do not.
   */
  public static rowTitle(title: string, kindLabel: string, subject: string): string {
    const prefix = `${kindLabel}${SEPARATOR}`;
    if (title.toLowerCase().startsWith(prefix.toLowerCase())) {
      const rest = title.slice(prefix.length).trim();
      if (rest === '') {
        return title;
      }
      return EventTitleUtils.isSubject(rest, subject) ? '' : rest;
    }
    const separator = title.indexOf(SEPARATOR);
    if (
      separator > 0 &&
      EventTitleUtils.isSubject(title.slice(separator + SEPARATOR.length), subject)
    ) {
      return '';
    }
    return title;
  }

  private static isSubject(text: string, subject: string): boolean {
    return text.trim().toLowerCase() === subject.trim().toLowerCase();
  }
}
