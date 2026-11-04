import {type IconName, type Nullable} from '@lifekit-hq/ui';

import {environment} from '../../../../environments/environment';
import {MERCHANT_CATEGORY_ICONS} from '../constants/subscription/merchant-icon.constants';

export class MerchantIconUtils {
  /**
   * Where a curated merchant's icon is served from: our own API, which fetches and caches it
   * server-side. Null when the row matched no curated merchant.
   */
  public static url(iconId: Nullable<number> | undefined): Nullable<string> {
    return iconId === null || iconId === undefined
      ? null
      : `${environment.apiBaseUrl}/subscriptions/icons/${iconId}`;
  }

  /** The fallback icon for a row's spending category; null when the category has none. */
  public static categoryIcon(category: Nullable<string> | undefined): Nullable<IconName> {
    return category && Object.hasOwn(MERCHANT_CATEGORY_ICONS, category)
      ? MERCHANT_CATEGORY_ICONS[category]
      : null;
  }
}
