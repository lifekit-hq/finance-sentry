import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, concatMap, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {AuthStore} from '../../../auth/store/auth.store';
import {type AccountClosureOptions} from '../../models/account-closure/account-closure.model';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {AccountService} from '../../services/account.service';
import {SettingsService} from '../../services/settings.service';
import {ProfileUtils} from '../../utils/profile.utils';

interface EffectsStore {
  profile: () => Nullable<UserProfile>;
  updateProfile: (partial: Partial<UserProfile>) => void;
  revertProfile: (previous: Partial<UserProfile>) => void;
  setProfile: (profile: UserProfile) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setProfileSaveLoading: () => void;
  setProfileSaveSuccess: () => void;
  setProfileSaveError: (errorCode: Nullable<string>) => void;
  profileSaveErrorMessage: () => string;
  closureOptions: () => Nullable<AccountClosureOptions>;
  setClosureOptions: (options: AccountClosureOptions) => void;
  setClosureLoading: () => void;
  setClosureSuccess: () => void;
  setClosureError: (errorCode: Nullable<string>) => void;
  closureErrorMessage: () => string;
}

interface ProfileWrite {
  change: Partial<UpdateProfileRequest>;
  confirm: boolean;
}

export function settingsEffects(store: EffectsStore) {
  const service = inject(SettingsService);
  const accountService = inject(AccountService);
  const authStore = inject(AuthStore);
  const toast = inject(ToastService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getProfile().pipe(
          tap(profile => {
            store.setProfile(profile);
            store.setSuccess();
            authStore.setProfileName(profile.firstName, profile.lastName);
          }),
          catchError(err => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  // One queue for every write of the profile: a toggle or select applies at once, a section Save
  // does the same and then confirms with a toast. Both take their change into the store now and
  // send the whole profile in order (concatMap), each request built from the store at send time so
  // no write carries a stale copy of another. A rejected change is rolled back alone, so a later
  // queued change is not lost with it.
  const write = rxMethod<ProfileWrite>(
    pipe(
      concatMap(({change, confirm}) => {
        const current = store.profile();
        if (!current) {
          return EMPTY;
        }
        const previous = ProfileUtils.pick(current, change);
        store.updateProfile(change);
        if (confirm) {
          store.setProfileSaveLoading();
        }
        return service.updateProfile(ProfileUtils.toRequest(store.profile() ?? current)).pipe(
          tap(saved => {
            if (confirm) {
              authStore.setProfileName(saved.firstName, saved.lastName);
              store.setProfileSaveSuccess();
              toast.show('Profile saved successfully', 'success');
            }
          }),
          catchError(err => {
            store.revertProfile(previous);
            store.setProfileSaveError(extractErrorCode(err));
            toast.show(store.profileSaveErrorMessage(), 'error');
            return EMPTY;
          })
        );
      })
    )
  );

  // A failed check leaves the options unset, which keeps the close action disabled.
  const loadClosureOptions = rxMethod<void>(
    pipe(
      switchMap(() =>
        accountService.getClosureOptions().pipe(
          tap(options => store.setClosureOptions(options)),
          catchError(() => EMPTY)
        )
      )
    )
  );

  // The server locks the account, revokes every session and deletes the stored provider keys
  // before it answers; signing out here only clears this tab's cookies and sends it to sign-in.
  const closeAccount = rxMethod<string>(
    pipe(
      tap(() => store.setClosureLoading()),
      exhaustMap(confirmEmail =>
        accountService.close({confirmEmail}).pipe(
          tap(() => {
            store.setClosureSuccess();
            const days = store.closureOptions()?.graceDays;
            toast.show(
              days
                ? `Account closed. An owner can reopen it within ${days} days.`
                : 'Account closed.',
              'info'
            );
            authStore.logout();
          }),
          catchError(err => {
            store.setClosureError(extractErrorCode(err));
            toast.show(store.closureErrorMessage(), 'error');
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    loadClosureOptions,
    closeAccount,
    applyChange(change: Partial<UpdateProfileRequest>): void {
      write({change, confirm: false});
    },
    saveProfile(change: Partial<UpdateProfileRequest>): void {
      write({change, confirm: true});
    },
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
  loadClosureOptions: () => void;
}

export function settingsHooks(store: HookStore): void {
  store.load();
  store.loadClosureOptions();
}
