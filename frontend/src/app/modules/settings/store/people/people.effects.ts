import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {type CreateInviteRequest, type Invite, type Person} from '../../models/person/person.model';
import {PeopleService} from '../../services/people.service';

interface EffectsStore {
  setLoading: () => void;
  setPeople: (people: Person[]) => void;
  setLoadError: (errorCode: Nullable<string>) => void;
  setInviting: () => void;
  setInvite: (invite: Invite) => void;
  setInviteError: (errorCode: Nullable<string>) => void;
  setRevoking: (userId: string) => void;
  setRevoked: () => void;
  setRevokeError: (errorCode: Nullable<string>) => void;
}

export function peopleEffects(store: EffectsStore) {
  const service = inject(PeopleService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.list().pipe(
          tap(people => store.setPeople(people)),
          catchError((err: unknown) => {
            store.setLoadError(extractErrorCode(err));
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
        tap(() => store.setInviting()),
        exhaustMap(request =>
          service.createInvite(request).pipe(
            tap(invite => {
              store.setInvite(invite);
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
  };
}

interface HookStore {
  load: () => void;
}

export function peopleHooks(store: HookStore): void {
  store.load();
}
