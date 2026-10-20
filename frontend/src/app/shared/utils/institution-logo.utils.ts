import {type Nullable} from '@lifekit-hq/ui';

import {
  BANK_NAME_DOMAINS,
  PROVIDER_DOMAINS,
} from '../constants/institution/institution-domains.constants';

const FAVICON_ENDPOINT = 'https://www.google.com/s2/favicons';
const FAVICON_SIZE = 64;

/**
 * Resolves a connected institution to a public favicon URL (Google's favicon
 * service) so we render real provider logos without hosting any images.
 * Returns null when the institution isn't recognised — callers fall back to the
 * initials avatar.
 */
export class InstitutionLogoUtils {
  public static faviconUrl(provider: Nullable<string>, name: Nullable<string>): Nullable<string> {
    const domain = InstitutionLogoUtils.resolveDomain(provider, name);
    if (!domain) {
      return null;
    }
    return `${FAVICON_ENDPOINT}?domain=${domain}&sz=${FAVICON_SIZE}`;
  }

  private static resolveDomain(
    provider: Nullable<string>,
    name: Nullable<string>
  ): Nullable<string> {
    const providerKey = provider?.trim().toLowerCase() ?? '';
    if (providerKey && PROVIDER_DOMAINS[providerKey]) {
      return PROVIDER_DOMAINS[providerKey];
    }

    const nameKey = name?.trim().toLowerCase() ?? '';
    if (!nameKey) {
      return null;
    }
    for (const [keyword, domain] of BANK_NAME_DOMAINS) {
      if (nameKey.includes(keyword)) {
        return domain;
      }
    }
    return null;
  }
}
