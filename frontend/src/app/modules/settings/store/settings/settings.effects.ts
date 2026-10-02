import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UpdateProfileRequest, type UserProfile} from '../../models/settings/settings.model';
import {type ChangePasswordRequest, SettingsService} from '../../services/settings.service';

interface EffectsStore {
  setProfile: (profile: UserProfile) => void;
  setLoadError: (errorCode: Nullable<string>) => void;
  setProfileSaving: (saving: boolean) => void;
  setProfileSaveError: (errorCode: Nullable<string>) => void;
  setPasswordSaving: (saving: boolean) => void;
  setPasswordSaveError: (errorCode: Nullable<string>) => void;
}

export function settingsEffects(store: EffectsStore) {
  const service = inject(SettingsService);
  const authStore = inject(AuthStore);

  return {
    load: rxMethod<void>(
      pipe(
        switchMap(() =>
          service.getProfile().pipe(
            tap(profile => {
              store.setProfile(profile);
              authStore.setProfileName(profile.firstName, profile.lastName);
            }),
            catchError(err => {
              store.setLoadError(extractErrorCode(err));
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
              store.setProfileSaving(false);
            }),
            catchError(err => {
              store.setProfileSaveError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    changePassword: rxMethod<ChangePasswordRequest>(
      pipe(
        tap(() => store.setPasswordSaving(true)),
        switchMap(request =>
          service.changePassword(request).pipe(
            tap(() => store.setPasswordSaving(false)),
            catchError(err => {
              store.setPasswordSaveError(extractErrorCode(err));
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
