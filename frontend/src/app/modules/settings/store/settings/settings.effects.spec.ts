import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ToastService} from '@lifekit-hq/ui';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {SettingsService} from '../../services/settings.service';
import {settingsEffects} from './settings.effects';

const PROFILE = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
} as unknown as UserProfile;
const REQUEST = {safeWithdrawalRate: 0.04} as unknown as UpdateProfileRequest;
const SAVE_FAILED = 'Failed to save profile.';

function buildStore() {
  const errorMessage = signal('');
  const profile = signal<Nullable<UserProfile>>(PROFILE);
  return {
    profile: () => profile(),
    updateProfile: vi.fn((partial: Partial<UserProfile>) =>
      profile.update(p => (p ? {...p, ...partial} : p))
    ),
    setProfile: vi.fn(),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setError: vi.fn(),
    setProfileSaveLoading: vi.fn(),
    setProfileSaveSuccess: vi.fn(),
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

  describe('applyChange', () => {
    it('patches the store at once and sends the whole profile without a success toast', () => {
      const store = buildStore();
      service.updateProfile.mockReturnValue(of(PROFILE));

      effects(store).applyChange({emailAlerts: true});

      expect(store.updateProfile).toHaveBeenCalledWith({emailAlerts: true});
      expect(service.updateProfile).toHaveBeenCalledWith(
        expect.objectContaining({firstName: 'Ada', emailAlerts: true, safeWithdrawalRate: 0.04})
      );
      expect(toast.show).not.toHaveBeenCalled();
    });

    it('toasts the failure and reloads the profile when the change is rejected', () => {
      const store = buildStore();
      service.updateProfile.mockReturnValue(
        throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'X'}}))
      );
      service.getProfile.mockReturnValue(of(PROFILE));

      effects(store).applyChange({baseCurrency: 'EUR'});

      expect(store.setProfileSaveError).toHaveBeenCalledWith('X');
      expect(toast.show).toHaveBeenCalledWith(SAVE_FAILED, 'error');
      expect(service.getProfile).toHaveBeenCalledTimes(1);
      expect(store.setProfile).toHaveBeenCalledWith(PROFILE);
    });

    it('sends a quick second change after the first, carrying both', () => {
      const store = buildStore();
      const first = new Subject<UserProfile>();
      service.updateProfile.mockReturnValueOnce(first).mockReturnValue(of(PROFILE));
      const fx = effects(store);

      fx.applyChange({emailAlerts: false});
      fx.applyChange({syncFailureAlerts: false});

      expect(service.updateProfile).toHaveBeenCalledTimes(1);
      first.next(PROFILE);
      first.complete();
      expect(service.updateProfile).toHaveBeenCalledTimes(2);
      expect(service.updateProfile).toHaveBeenLastCalledWith(
        expect.objectContaining({emailAlerts: false, syncFailureAlerts: false})
      );
    });
  });
});
