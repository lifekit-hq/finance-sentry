export class AuthUtils {
  /** The API URL that sends the browser to the identity provider, carrying the in-app path to return to. */
  public static oidcStartUrl(apiBaseUrl: string, returnUrl: Nullable<string>): string {
    const start = `${apiBaseUrl}/auth/oidc/start`;
    return returnUrl ? `${start}?returnUrl=${encodeURIComponent(returnUrl)}` : start;
  }
}
