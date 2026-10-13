import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, concatMap, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {SettingsService} from '../../services/settings.service';
import {ProfileUtils} from '../../utils/profile.utils';

interface EffectsStore {
  profile: () => Nullable<UserProfile>;
  updateProfile: (partial: Partial<UserProfile>) => void;
  setProfile: (profile: UserProfile) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setProfileSaveLoading: () => void;
  setProfileSaveSuccess: () => void;
  setProfileSaveError: (errorCode: Nullable<string>) => void;
  profileSaveErrorMessage: () => string;
}

export function settingsEffects(store: EffectsStore) {
  const service = inject(SettingsService);
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

  return {
    load,
    // A toggle or select applies at once: the store takes the change now and the full profile goes
    // out in order (concatMap), each request built from the store at send time so a quick second
    // change never overwrites the first. A failure reloads the profile and says so in a toast.
    applyChange: rxMethod<Partial<UpdateProfileRequest>>(
      pipe(
        tap(change => store.updateProfile(change)),
        concatMap(() => {
          const profile = store.profile();
          if (!profile) {
            return EMPTY;
          }
          return service.updateProfile(ProfileUtils.toRequest(profile)).pipe(
            catchError(err => {
              store.setProfileSaveError(extractErrorCode(err));
              toast.show(store.profileSaveErrorMessage(), 'error');
              load();
              return EMPTY;
            })
          );
        })
      )
    ),
    saveProfile: rxMethod<UpdateProfileRequest>(
      pipe(
        tap(() => store.setProfileSaveLoading()),
        switchMap(request =>
          service.updateProfile(request).pipe(
            tap(profile => {
              store.setProfile(profile);
              authStore.setProfileName(profile.firstName, profile.lastName);
              store.setProfileSaveSuccess();
              toast.show('Profile saved successfully', 'success');
            }),
            catchError(err => {
              store.setProfileSaveError(extractErrorCode(err));
              toast.show(store.profileSaveErrorMessage(), 'error');
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function settingsHooks(store: HookStore): void {
  store.load();
}
