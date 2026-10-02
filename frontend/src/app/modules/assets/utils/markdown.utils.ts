const HTML_ESCAPES: Readonly<Record<string, string>> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
};

export class MarkdownUtils {
  /**
   * Minimal, safe inline markdown for LLM prose: all HTML is escaped first, then **bold** and
   * *italic* become tags and newlines become <br>. Only tags emitted here can ever appear.
   */
  public static toSafeHtml(source: string): string {
    const escaped = source.replace(/[&<>"']/g, ch => HTML_ESCAPES[ch]);
    return escaped
      .replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>')
      .replace(/(^|[^*])\*([^*\n]+)\*(?!\*)/g, '$1<em>$2</em>')
      .replace(/\r?\n/g, '<br>');
  }
}
