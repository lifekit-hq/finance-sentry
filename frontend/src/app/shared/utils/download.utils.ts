export class DownloadUtils {
  /** `finance-sentry-export-2026-11-03.json`: the export's file name for a given day (UTC, `YYYY-MM-DD`). */
  public static exportFileName(now: Date): string {
    return `finance-sentry-export-${now.toISOString().split('T')[0]}.json`;
  }

  /** Hands a blob to the browser as a file download under `fileName`. */
  public static saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }
}
