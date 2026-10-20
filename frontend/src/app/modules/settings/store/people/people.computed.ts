import {computed, inject, type Signal} from '@angular/core';
import {type AsyncStatus, ErrorMessageService} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {OWNER_ROLE} from '../../../../shared/constants/role/role.constants';
import {DateUtils} from '../../../../shared/utils/date.utils';
import {AuthStore} from '../../../auth/store/auth.store';
import {type Person, type PersonRow} from '../../models/person/person.model';

interface StateSignals {
  people: Signal<Person[]>;
  peopleStatus: Signal<AsyncStatus>;
  revokeErrorCode: Signal<Nullable<string>>;
  reopenErrorCode: Signal<Nullable<string>>;
}

function sublabel(person: Person, rolesLabel: string): string {
  if (person.status !== 'Closed' || !person.purgeDueAt) {
    return rolesLabel;
  }
  // The purge is at most the grace period away, so the day needs no year (and fits a phone row).
  const day = DateUtils.dayLabel(DateUtils.isoDateLocal(new Date(person.purgeDueAt)), false);
  return `${rolesLabel} · erased ${day}`;
}

export function peopleComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  const currentUserId = inject(AuthStore).userId;

  return {
    isLoading: computed(() => store.peopleStatus() === 'loading' && store.people().length === 0),
    rows: computed<PersonRow[]>(() =>
      store.people().map(person => {
        const rolesLabel = person.roles.length > 0 ? person.roles.join(', ') : 'No role';
        return {
          ...person,
          rolesLabel,
          sublabel: sublabel(person, rolesLabel),
          canRevoke:
            person.status !== 'Revoked' &&
            person.status !== 'Closed' &&
            person.id !== currentUserId() &&
            !person.roles.includes(OWNER_ROLE),
          canReopen: person.status === 'Closed',
        };
      })
    ),
    revokeErrorMessage: computed(() => {
      const code = store.revokeErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not revoke access.') : '';
    }),
    reopenErrorMessage: computed(() => {
      const code = store.reopenErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not reopen the account.') : '';
    }),
  };
}
