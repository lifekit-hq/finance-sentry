import {computed, inject, type Signal} from '@angular/core';
import {type AsyncStatus, ErrorMessageService} from '@lifekit-hq/core';

import {OWNER_ROLE} from '../../../../shared/constants/role/role.constants';
import {AuthStore} from '../../../auth/store/auth.store';
import {type Person, type PersonRow} from '../../models/person/person.model';

interface StateSignals {
  people: Signal<Person[]>;
  peopleStatus: Signal<AsyncStatus>;
  revokeErrorCode: Signal<Nullable<string>>;
}

export function peopleComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  const currentUserId = inject(AuthStore).userId;

  return {
    isLoading: computed(() => store.peopleStatus() === 'loading' && store.people().length === 0),
    rows: computed<PersonRow[]>(() =>
      store.people().map(person => ({
        ...person,
        rolesLabel: person.roles.length > 0 ? person.roles.join(', ') : 'No role',
        canRevoke:
          person.status !== 'Revoked' &&
          person.id !== currentUserId() &&
          !person.roles.includes(OWNER_ROLE),
      }))
    ),
    revokeErrorMessage: computed(() => {
      const code = store.revokeErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not revoke access.') : '';
    }),
  };
}
