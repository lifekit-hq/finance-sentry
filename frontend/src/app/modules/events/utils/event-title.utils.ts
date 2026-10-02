export class EventTitleUtils {
  /**
   * Title text for an event row once the kind tag and subject are already shown: drops a leading
   * "<kindLabel>: " and returns '' when what remains is just the subject.
   */
  public static rowTitle(title: string, kindLabel: string, subject: string): string {
    const prefix = `${kindLabel}: `;
    if (!title.toLowerCase().startsWith(prefix.toLowerCase())) {
      return title;
    }
    const rest = title.slice(prefix.length).trim();
    if (rest === '') {
      return title;
    }
    return rest.toLowerCase() === subject.trim().toLowerCase() ? '' : rest;
  }
}
