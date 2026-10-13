import {describe, expect, it} from 'vitest';

import {type UserProfile} from '../models/settings/settings.model';
import {ProfileUtils} from './profile.utils';

const PROFILE: UserProfile = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  email: 'ada@example.com',
  baseCurrency: 'EUR',
  emailAlerts: true,
  lowBalanceAlerts: false,
  lowBalanceThreshold: 100,
  syncFailureAlerts: true,
  watchlistAnalystAlerts: false,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  twoFactor: true,
};

describe('ProfileUtils', () => {
  it('keeps every writable field of the profile', () => {
    expect(ProfileUtils.toRequest(PROFILE)).toEqual({
      firstName: 'Ada',
      lastName: 'Lovelace',
      baseCurrency: 'EUR',
      emailAlerts: true,
      lowBalanceAlerts: false,
      lowBalanceThreshold: 100,
      syncFailureAlerts: true,
      watchlistAnalystAlerts: false,
      safeWithdrawalRate: 0.04,
      realAnnualReturn: 0.05,
    });
  });

  it('drops the read-only email and two-factor fields', () => {
    const request = ProfileUtils.toRequest(PROFILE) as unknown as Record<string, unknown>;

    expect(request).not.toHaveProperty('email');
    expect(request).not.toHaveProperty('twoFactor');
  });

  it('picks the current values of the fields a change names', () => {
    expect(ProfileUtils.pick(PROFILE, {firstName: 'Grace', emailAlerts: false})).toEqual({
      firstName: 'Ada',
      emailAlerts: true,
    });
  });
});
