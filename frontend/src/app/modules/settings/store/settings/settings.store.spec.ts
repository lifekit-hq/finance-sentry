import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {ToastService} from '@lifekit-hq/ui';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {AuthStore} from '../../../auth/store/auth.store';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {AccountService} from '../../services/account.service';
import {SettingsService} from '../../services/settings.service';
import {SettingsStore} from './settings.store';

const PROFILE = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  baseCurrency: 'USD',
} as unknown as UserProfile;
const REQUEST = {safeWithdrawalRate: 0.04} as unknown as UpdateProfileRequest;

describe('SettingsStore', () => {
  let service: {getProfile: ReturnType<typeof vi.fn>; updateProfile: ReturnType<typeof vi.fn>};
  let account: {getClosureOptions: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    service = {getProfile: vi.fn(), updateProfile: vi.fn()};
    account = {getClosureOptions: vi.fn().mockReturnValue(NEVER), close: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        SettingsStore,
        {provide: SettingsService, useValue: service},
        {provide: AccountService, useValue: account},
        {provide: AuthStore, useValue: {setProfileName: vi.fn()}},
        {provide: ToastService, useValue: {show: vi.fn()}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
  });

  it('is loading until the profile arrives', () => {
    service.getProfile.mockReturnValue(NEVER);

    const store = TestBed.inject(SettingsStore);

    expect(store.isLoading()).toBe(true);
    expect(store.profile()).toBeNull();
  });

  it('holds the profile and settles on success', () => {
    service.getProfile.mockReturnValue(of(PROFILE));

    const store = TestBed.inject(SettingsStore);

    expect(store.profile()).toEqual(PROFILE);
    expect(store.status()).toBe('success');
    expect(store.fullName()).toBe('Ada Lovelace');
  });

  it('keeps the page empty and reports the default message when the load fails', () => {
    service.getProfile.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));

    const store = TestBed.inject(SettingsStore);

    expect(store.profile()).toBeNull();
    expect(store.hasError()).toBe(true);
    expect(store.errorMessage()).toBe('Failed to load profile.');
  });

  it('marks the save as loading until the server answers, without touching the load status', () => {
    service.getProfile.mockReturnValue(of(PROFILE));
    service.updateProfile.mockReturnValue(NEVER);
    const store = TestBed.inject(SettingsStore);

    store.saveProfile(REQUEST);

    expect(store.isProfileSaveLoading()).toBe(true);
    expect(store.status()).toBe('success');
  });

  it('settles the save on success and keeps the saved profile', () => {
    service.getProfile.mockReturnValue(of(PROFILE));
    service.updateProfile.mockReturnValue(of(PROFILE));
    const store = TestBed.inject(SettingsStore);

    store.saveProfile(REQUEST);

    expect(store.profileSaveStatus()).toBe('success');
    expect(store.isProfileSaveLoading()).toBe(false);
    expect(store.profileSaveErrorMessage()).toBe('');
  });

  it('resolves a known save error code through the registry', () => {
    service.getProfile.mockReturnValue(of(PROFILE));
    service.updateProfile.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'FORBIDDEN'}}))
    );
    const store = TestBed.inject(SettingsStore);

    store.saveProfile(REQUEST);

    expect(store.hasProfileSaveError()).toBe(true);
    expect(store.profileSaveErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['FORBIDDEN']);
    expect(store.status()).toBe('success');
  });

  it('rolls a rejected change back and bumps the revision, leaving the rest as it was', () => {
    service.getProfile.mockReturnValue(of(PROFILE));
    service.updateProfile.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));
    const store = TestBed.inject(SettingsStore);

    store.applyChange({baseCurrency: 'EUR'});

    expect(store.profile()?.baseCurrency).toBe('USD');
    expect(store.profileRevision()).toBe(1);
  });

  it('falls back to the save default for an unknown save error', () => {
    service.getProfile.mockReturnValue(of(PROFILE));
    service.updateProfile.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));
    const store = TestBed.inject(SettingsStore);

    store.saveProfile(REQUEST);

    expect(store.profileSaveErrorMessage()).toBe('Failed to save profile.');
  });

  describe('account closure', () => {
    it('keeps the close action off until the server says this account may close', () => {
      service.getProfile.mockReturnValue(of(PROFILE));

      const store = TestBed.inject(SettingsStore);

      expect(store.canCloseAccount()).toBe(false);
      expect(store.closureDescription()).toBe('Close your account and sign out everywhere.');
    });

    it('explains the grace period when the account may close', () => {
      service.getProfile.mockReturnValue(of(PROFILE));
      account.getClosureOptions.mockReturnValue(of({canClose: true, reason: null, graceDays: 30}));

      const store = TestBed.inject(SettingsStore);

      expect(store.canCloseAccount()).toBe(true);
      expect(store.closureDescription()).toContain('kept for 30 days so an owner can reopen it');
    });

    it('shows the reason when the account may not close', () => {
      service.getProfile.mockReturnValue(of(PROFILE));
      account.getClosureOptions.mockReturnValue(
        of({canClose: false, reason: 'You are the only owner.', graceDays: 30})
      );

      const store = TestBed.inject(SettingsStore);

      expect(store.canCloseAccount()).toBe(false);
      expect(store.closureDescription()).toBe('You are the only owner.');
    });

    it('resolves a refused close through the registry', () => {
      service.getProfile.mockReturnValue(of(PROFILE));
      account.close.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: {errorCode: 'CLOSURE_CONFIRMATION_MISMATCH'},
            })
        )
      );
      const store = TestBed.inject(SettingsStore);

      store.closeAccount('wrong@example.com');

      expect(store.closureErrorMessage()).toBe(
        'Type your account email exactly to confirm closing the account.'
      );
    });
  });
});
