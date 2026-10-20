import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable, ToastService} from '@lifekit-hq/ui';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {type AccountClosureOptions} from '../../models/account-closure/account-closure.model';
import {type UserProfile} from '../../models/settings/settings.model';
import {AccountService} from '../../services/account.service';
import {SettingsService} from '../../services/settings.service';
import {settingsEffects, settingsHooks} from './settings.effects';

const PROFILE = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
} as unknown as UserProfile;
const SAVE_FAILED = 'Failed to save profile.';
const CLOSE_FAILED = 'Could not close the account.';
const CAN_CLOSE: AccountClosureOptions = {canClose: true, reason: null, graceDays: 30};

function buildStore() {
  const errorMessage = signal('');
  const closureError = signal('');
  const profile = signal<Nullable<UserProfile>>(PROFILE);
  const closureOptions = signal<Nullable<AccountClosureOptions>>(CAN_CLOSE);
  return {
    profile: () => profile(),
    updateProfile: vi.fn((partial: Partial<UserProfile>) =>
      profile.update(p => (p ? {...p, ...partial} : p))
    ),
    setProfile: vi.fn(),
    revertProfile: vi.fn((previous: Partial<UserProfile>) =>
      profile.update(p => (p ? {...p, ...previous} : p))
    ),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setError: vi.fn(),
    setProfileSaveLoading: vi.fn(),
    setProfileSaveSuccess: vi.fn(),
    setProfileSaveError: vi.fn(() => errorMessage.set(SAVE_FAILED)),
    profileSaveErrorMessage: () => errorMessage(),
    closureOptions: () => closureOptions(),
    setClosureOptions: vi.fn((options: AccountClosureOptions) => closureOptions.set(options)),
    setClosureLoading: vi.fn(),
    setClosureSuccess: vi.fn(),
    setClosureError: vi.fn(() => closureError.set(CLOSE_FAILED)),
    closureErrorMessage: () => closureError(),
  };
}

describe('settingsEffects', () => {
  let service: {getProfile: ReturnType<typeof vi.fn>; updateProfile: ReturnType<typeof vi.fn>};
  let toast: {show: ReturnType<typeof vi.fn>};
  let account: {getClosureOptions: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn>};
  let auth: {setProfileName: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    service = {getProfile: vi.fn(), updateProfile: vi.fn()};
    toast = {show: vi.fn()};
    account = {getClosureOptions: vi.fn(), close: vi.fn()};
    auth = {setProfileName: vi.fn(), logout: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: SettingsService, useValue: service},
        {provide: AccountService, useValue: account},
        {provide: AuthStore, useValue: auth},
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

  describe('saveProfile', () => {
    it('takes the section at once and toasts success only once the server has answered', () => {
      const store = buildStore();
      const answer = new Subject<UserProfile>();
      service.updateProfile.mockReturnValue(answer);

      effects(store).saveProfile({firstName: 'Grace'});

      expect(store.updateProfile).toHaveBeenCalledWith({firstName: 'Grace'});
      expect(service.updateProfile).toHaveBeenCalledWith(
        expect.objectContaining({firstName: 'Grace', lastName: 'Lovelace'})
      );
      expect(toast.show).not.toHaveBeenCalled();
      answer.next({...PROFILE, firstName: 'Grace'});
      answer.complete();
      expect(store.setProfileSaveSuccess).toHaveBeenCalled();
      expect(toast.show).toHaveBeenCalledTimes(1);
      expect(toast.show).toHaveBeenCalledWith('Profile saved successfully', 'success');
    });

    it('toasts the failure, never a success, and puts the section back when rejected', () => {
      const store = buildStore();
      service.updateProfile.mockReturnValue(
        throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'X'}}))
      );

      effects(store).saveProfile({firstName: 'Grace'});

      expect(store.setProfileSaveError).toHaveBeenCalledWith('X');
      expect(store.revertProfile).toHaveBeenCalledWith({firstName: 'Ada'});
      expect(store.profile()?.firstName).toBe('Ada');
      expect(toast.show).toHaveBeenCalledTimes(1);
      expect(toast.show).toHaveBeenCalledWith(SAVE_FAILED, 'error');
    });

    it('does not send a toggle made while a save is in flight with the old section', () => {
      const store = buildStore();
      const save = new Subject<UserProfile>();
      service.updateProfile.mockReturnValueOnce(save).mockReturnValue(of(PROFILE));
      const fx = effects(store);

      fx.saveProfile({firstName: 'Grace'});
      fx.applyChange({emailAlerts: false});

      expect(service.updateProfile).toHaveBeenCalledTimes(1);
      save.next(PROFILE);
      save.complete();
      expect(service.updateProfile).toHaveBeenLastCalledWith(
        expect.objectContaining({firstName: 'Grace', emailAlerts: false})
      );
    });
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

    it('toasts the failure and rolls back only the rejected change', () => {
      const store = buildStore();
      service.updateProfile.mockReturnValue(
        throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'X'}}))
      );

      effects(store).applyChange({baseCurrency: 'EUR'});

      expect(store.setProfileSaveError).toHaveBeenCalledWith('X');
      expect(toast.show).toHaveBeenCalledWith(SAVE_FAILED, 'error');
      expect(store.revertProfile).toHaveBeenCalledWith({baseCurrency: undefined});
      expect(service.getProfile).not.toHaveBeenCalled();
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

  describe('loadClosureOptions', () => {
    it('stores whether this account may be closed', () => {
      const store = buildStore();
      const options = {canClose: false, reason: 'You are the only owner.', graceDays: 30};
      account.getClosureOptions.mockReturnValue(of(options));

      effects(store).loadClosureOptions();

      expect(store.setClosureOptions).toHaveBeenCalledWith(options);
    });

    it('leaves the options unset when the check fails', () => {
      const store = buildStore();
      account.getClosureOptions.mockReturnValue(
        throwError(() => new HttpErrorResponse({status: 500}))
      );

      effects(store).loadClosureOptions();

      expect(store.setClosureOptions).not.toHaveBeenCalled();
    });
  });

  describe('closeAccount', () => {
    it('sends the typed email, then signs this tab out', () => {
      const store = buildStore();
      account.close.mockReturnValue(
        of({closedAt: '2026-10-14T10:00:00Z', purgeDueAt: '2026-11-13T10:00:00Z'})
      );

      effects(store).closeAccount('ada@example.com');

      expect(store.setClosureLoading).toHaveBeenCalled();
      expect(account.close).toHaveBeenCalledWith({confirmEmail: 'ada@example.com'});
      expect(store.setClosureSuccess).toHaveBeenCalled();
      expect(toast.show).toHaveBeenCalledWith(
        'Account closed. An owner can reopen it within 30 days.',
        'info'
      );
      expect(auth.logout).toHaveBeenCalled();
    });

    it('stays signed in and toasts the reason when the server refuses', () => {
      const store = buildStore();
      account.close.mockReturnValue(
        throwError(
          () => new HttpErrorResponse({status: 409, error: {errorCode: 'LAST_OWNER_CANNOT_CLOSE'}})
        )
      );

      effects(store).closeAccount('ada@example.com');

      expect(store.setClosureError).toHaveBeenCalledWith('LAST_OWNER_CANNOT_CLOSE');
      expect(toast.show).toHaveBeenCalledWith(CLOSE_FAILED, 'error');
      expect(auth.logout).not.toHaveBeenCalled();
    });
  });

  it('settingsHooks loads the profile and the closure options', () => {
    const store = {...buildStore(), load: vi.fn(), loadClosureOptions: vi.fn()};

    settingsHooks(store);

    expect(store.load).toHaveBeenCalled();
    expect(store.loadClosureOptions).toHaveBeenCalled();
  });
});
