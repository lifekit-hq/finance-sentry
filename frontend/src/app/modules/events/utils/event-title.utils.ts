export class EventTitleUtils {
  /** Drops a leading "<kindLabel>: " from a title when the kind tag already shows that kind. */
  public static stripKindPrefix(title: string, kindLabel: string): string {
    const prefix = `${kindLabel}: `;
    if (!title.toLowerCase().startsWith(prefix.toLowerCase())) {
      return title;
    }
    const rest = title.slice(prefix.length).trim();
    return rest === '' ? title : rest;
  }
}
