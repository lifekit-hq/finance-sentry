import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {ThemeService, ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {SettingsService} from '../../services/settings.service';

interface EffectsStore {
  setProfile: (profile: UserProfile) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setProfileSaving: (saving: boolean) => void;
  setProfileSaveError: (errorCode: Nullable<string>) => void;
  profileSaveErrorMessage: () => string;
}

export function settingsEffects(store: EffectsStore) {
  const service = inject(SettingsService);
  const authStore = inject(AuthStore);
  const themeService = inject(ThemeService);
  const toast = inject(ToastService);

  return {
    load: rxMethod<void>(
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
    ),
    saveProfile: rxMethod<UpdateProfileRequest>(
      pipe(
        tap(() => store.setProfileSaving(true)),
        switchMap(request =>
          service.updateProfile(request).pipe(
            tap(profile => {
              store.setProfile(profile);
              authStore.setProfileName(profile.firstName, profile.lastName);
              themeService.setPreference(profile.theme);
              store.setProfileSaving(false);
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
