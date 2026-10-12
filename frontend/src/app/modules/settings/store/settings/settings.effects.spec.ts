import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ThemeService, ToastService} from '@lifekit-hq/ui';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {SettingsService} from '../../services/settings.service';
import {settingsEffects} from './settings.effects';

const PROFILE = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  theme: 'dark',
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
} as unknown as UserProfile;
const REQUEST = {safeWithdrawalRate: 0.04} as unknown as UpdateProfileRequest;
const SAVE_FAILED = 'Failed to save profile.';

function buildStore() {
  const errorMessage = signal('');
  return {
    setProfile: vi.fn(),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setError: vi.fn(),
    setProfileSaving: vi.fn(),
    setProfileSaveError: vi.fn(() => errorMessage.set(SAVE_FAILED)),
    profileSaveErrorMessage: () => errorMessage(),
  };
}

describe('settingsEffects', () => {
  let service: {getProfile: ReturnType<typeof vi.fn>; updateProfile: ReturnType<typeof vi.fn>};
  let toast: {show: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    service = {getProfile: vi.fn(), updateProfile: vi.fn()};
    toast = {show: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: SettingsService, useValue: service},
        {provide: AuthStore, useValue: {setProfileName: vi.fn()}},
        {provide: ThemeService, useValue: {setPreference: vi.fn()}},
        {provide: ToastService, useValue: toast},
      ],
    });
  });

  function effects(store: ReturnType<typeof buildStore>) {
    return TestBed.runInInjectionContext(() => settingsEffects(store));
  }

  describe('load', () => {
    it('stores the profile and marks the load a success', () => {
      const store = buildStore();
      service.getProfile.mockReturnValue(of(PROFILE));

      effects(store).load();

      expect(store.setLoading).toHaveBeenCalled();
      expect(store.setProfile).toHaveBeenCalledWith(PROFILE);
      expect(store.setSuccess).toHaveBeenCalled();
    });

    it('records the error code when the load fails', () => {
      const store = buildStore();
      service.getProfile.mockReturnValue(
        throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'BOOM'}}))
      );

      effects(store).load();

      expect(store.setError).toHaveBeenCalledWith('BOOM');
      expect(store.setSuccess).not.toHaveBeenCalled();
    });
  });

  it('toasts success only once the server has answered', () => {
    const store = buildStore();
    service.updateProfile.mockReturnValue(of(PROFILE));

    effects(store).saveProfile(REQUEST);

    expect(store.setProfile).toHaveBeenCalledWith(PROFILE);
    expect(toast.show).toHaveBeenCalledTimes(1);
    expect(toast.show).toHaveBeenCalledWith('Profile saved successfully', 'success');
  });

  it('toasts the failure and never a success when the save is rejected', () => {
    const store = buildStore();
    service.updateProfile.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'X'}}))
    );

    effects(store).saveProfile(REQUEST);

    expect(store.setProfileSaveError).toHaveBeenCalledWith('X');
    expect(toast.show).toHaveBeenCalledTimes(1);
    expect(toast.show).toHaveBeenCalledWith(SAVE_FAILED, 'error');
  });
});
