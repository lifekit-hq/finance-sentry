import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {type CreateInviteRequest, type Invite, type Person} from '../../models/person/person.model';
import {PeopleService} from '../../services/people.service';

interface EffectsStore {
  setPeopleLoading: () => void;
  setPeopleSuccess: () => void;
  setPeopleError: (errorCode: Nullable<string>) => void;
  setPeople: (people: Person[]) => void;
  setInviteLoading: () => void;
  setInviteSuccess: () => void;
  setInviteError: (errorCode: Nullable<string>) => void;
  setInvite: (invite: Invite) => void;
  dismissInvite: () => void;
  setRevoking: (userId: string) => void;
  setRevoked: () => void;
  setRevokeError: (errorCode: Nullable<string>) => void;
  setReopening: (userId: string) => void;
  setReopened: () => void;
  setReopenError: (errorCode: Nullable<string>) => void;
}

export function peopleEffects(store: EffectsStore) {
  const service = inject(PeopleService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setPeopleLoading()),
      switchMap(() =>
        service.list().pipe(
          tap(people => {
            store.setPeople(people);
            store.setPeopleSuccess();
          }),
          catchError((err: unknown) => {
            store.setPeopleError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    createInvite: rxMethod<CreateInviteRequest>(
      pipe(
        tap(() => {
          store.setInviteLoading();
          store.dismissInvite();
        }),
        exhaustMap(request =>
          service.createInvite(request).pipe(
            tap(invite => {
              store.setInvite(invite);
              store.setInviteSuccess();
              load();
            }),
            catchError((err: unknown) => {
              store.setInviteError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    revoke: rxMethod<string>(
      pipe(
        tap(userId => store.setRevoking(userId)),
        exhaustMap(userId =>
          service.revoke(userId).pipe(
            tap(() => {
              store.setRevoked();
              load();
            }),
            catchError((err: unknown) => {
              store.setRevokeError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    reopen: rxMethod<string>(
      pipe(
        tap(userId => store.setReopening(userId)),
        exhaustMap(userId =>
          service.reopen(userId).pipe(
            tap(() => {
              store.setReopened();
              load();
            }),
            catchError((err: unknown) => {
              store.setReopenError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore {
  load: () => void;
}

export function peopleHooks(store: HookStore): void {
  store.load();
}
