import {type UpdateProfileRequest, type UserProfile} from '../models/settings/settings.model';

export class ProfileUtils {
  /** The writable part of a profile: what `PUT /profile` takes, without email and two-factor. */
  public static toRequest(profile: UserProfile): UpdateProfileRequest {
    return {
      firstName: profile.firstName,
      lastName: profile.lastName,
      baseCurrency: profile.baseCurrency,
      emailAlerts: profile.emailAlerts,
      lowBalanceAlerts: profile.lowBalanceAlerts,
      lowBalanceThreshold: profile.lowBalanceThreshold,
      syncFailureAlerts: profile.syncFailureAlerts,
      watchlistAnalystAlerts: profile.watchlistAnalystAlerts,
      safeWithdrawalRate: profile.safeWithdrawalRate,
      realAnnualReturn: profile.realAnnualReturn,
      hideEmptyBalances: profile.hideEmptyBalances,
      hideSmallBalances: profile.hideSmallBalances,
      smallBalanceThreshold: profile.smallBalanceThreshold,
    };
  }

  /** The current values of the fields `change` names, to put back if the change is rejected. */
  public static pick(
    profile: UserProfile,
    change: Partial<UpdateProfileRequest>
  ): Partial<UserProfile> {
    const keys = Object.keys(change) as (keyof UpdateProfileRequest)[];
    return Object.fromEntries(keys.map(key => [key, profile[key]])) as Partial<UserProfile>;
  }
}
