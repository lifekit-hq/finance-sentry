import {type Nullable} from '@lifekit-hq/ui';
export class AccountClosureUtils {
  /** Whether the typed text confirms closing the account: the account email, ignoring case and outer spaces. */
  public static confirms(typed: Nullable<string>, email: Nullable<string>): boolean {
    const expected = email?.trim().toLowerCase();
    return !!expected && typed?.trim().toLowerCase() === expected;
  }
}
