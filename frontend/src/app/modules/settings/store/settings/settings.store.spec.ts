import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {ThemeService, ToastService} from '@lifekit-hq/ui';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {AuthStore} from '../../../auth/store/auth.store';
import {type UserProfile} from '../../models/settings/settings.model';
import {SettingsService} from '../../services/settings.service';
import {SettingsStore} from './settings.store';

const PROFILE = {firstName: 'Ada', lastName: 'Lovelace', theme: 'dark'} as unknown as UserProfile;

describe('SettingsStore', () => {
  let service: {getProfile: ReturnType<typeof vi.fn>; updateProfile: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    service = {getProfile: vi.fn(), updateProfile: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        SettingsStore,
        {provide: SettingsService, useValue: service},
        {provide: AuthStore, useValue: {setProfileName: vi.fn()}},
        {provide: ThemeService, useValue: {setPreference: vi.fn()}},
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
});
