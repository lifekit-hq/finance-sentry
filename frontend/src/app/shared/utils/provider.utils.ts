import {PROVIDER_CATALOG} from '../constants/providers/providers.constants';

export class ProviderUtils {
  /** Display name for a provider slug; an unknown slug is shown as-is. */
  public static label(slug: string): string {
    return PROVIDER_CATALOG.find(p => p.slug === slug)?.displayName ?? slug;
  }
}
