import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {ToastService} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UserProfile} from '../../models/settings/settings.model';
import {PushStore} from '../../store/push/push.store';
import {SettingsStore} from '../../store/settings/settings.store';
import {SettingsComponent} from './settings.component';

const profile = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  baseCurrency: 'USD',
  theme: 'system',
  emailAlerts: true,
  lowBalanceAlerts: true,
  lowBalanceThreshold: 100,
  syncFailureAlerts: true,
  watchlistAnalystAlerts: false,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
} as unknown as UserProfile;

function create() {
  const store = {
    profile: signal<UserProfile | null>(profile),
    saveProfile: vi.fn(),
    updateProfile: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AuthStore, useValue: {canManageUsers: signal(false)}},
      {provide: ToastService, useValue: {show: vi.fn()}},
    ],
  });
  TestBed.overrideComponent(SettingsComponent, {
    set: {
      providers: [
        {provide: SettingsStore, useValue: store},
        {provide: PushStore, useValue: {}},
      ],
      template: '',
    },
  });
  return {store, component: TestBed.createComponent(SettingsComponent).componentInstance};
}

describe('SettingsComponent financial independence assumptions', () => {
  it('sends both assumptions when the profile is saved', () => {
    const {store, component} = create();
    component.saveProfile();
    expect(store.saveProfile).toHaveBeenCalledWith(
      expect.objectContaining({safeWithdrawalRate: 0.04, realAnnualReturn: 0.05})
    );
  });

  it('stores a typed withdrawal rate as a fraction', () => {
    const {store, component} = create();
    component.setWithdrawalRate('3.5');
    expect(store.updateProfile).toHaveBeenCalledWith({safeWithdrawalRate: 0.035});
  });

  it('ignores an out-of-range withdrawal rate', () => {
    const {store, component} = create();
    component.setWithdrawalRate('40');
    component.setWithdrawalRate('abc');
    expect(store.updateProfile).not.toHaveBeenCalled();
  });

  it('stores a typed real return as a fraction and ignores out-of-range input', () => {
    const {store, component} = create();
    component.setRealReturn('6');
    component.setRealReturn('-1');
    expect(store.updateProfile).toHaveBeenCalledTimes(1);
    expect(store.updateProfile).toHaveBeenCalledWith({realAnnualReturn: 0.06});
  });

  it('flags 12 in the withdrawal-rate field and blocks Save until it is corrected', () => {
    const {store, component} = create();
    component.setWithdrawalRate('12');
    expect(component.withdrawalRateError()).toContain('1');
    expect(component.assumptionsInvalid()).toBe(true);
    component.saveProfile();
    expect(store.saveProfile).not.toHaveBeenCalled();
    component.setWithdrawalRate('4');
    expect(component.withdrawalRateError()).toBe('');
    expect(component.assumptionsInvalid()).toBe(false);
    component.saveProfile();
    expect(store.saveProfile).toHaveBeenCalledTimes(1);
  });

  it('keeps showing what the user typed after a rejected value', () => {
    const {component} = create();
    component.setWithdrawalRate('12');
    expect(component.withdrawalRateText(0.04)).toBe('12');
  });

  it('flags a non-numeric real return', () => {
    const {component} = create();
    component.setRealReturn('abc');
    expect(component.realReturnError()).not.toBe('');
    expect(component.assumptionsInvalid()).toBe(true);
  });
});
