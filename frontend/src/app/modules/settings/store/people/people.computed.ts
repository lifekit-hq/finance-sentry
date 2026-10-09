import {DOCUMENT} from '@angular/common';
import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {OWNER_ROLE} from '../../../../shared/constants/role/role.constants';
import {AuthStore} from '../../../auth/store/auth.store';
import {type Invite, type Person, type PersonRow} from '../../models/person/person.model';
import {InviteLinkUtils} from '../../utils/invite-link.utils';

interface StateSignals {
  people: Signal<Person[]>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  inviteStatus: Signal<AsyncStatus>;
  inviteErrorCode: Signal<Nullable<string>>;
  lastInvite: Signal<Nullable<Invite>>;
  revokeErrorCode: Signal<Nullable<string>>;
}

export function peopleComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  const currentUserId = inject(AuthStore).userId;
  const origin = inject(DOCUMENT).location.origin;

  return {
    isLoading: computed(() => store.status() === 'loading' && store.people().length === 0),
    isInviting: computed(() => store.inviteStatus() === 'loading'),
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
    inviteLink: computed(() => {
      const invite = store.lastInvite();
      return invite ? InviteLinkUtils.build(origin, invite.userId, invite.token) : null;
    }),
    loadErrorMessage: computed(() =>
      store.status() === 'error'
        ? (errorMessages.resolve(store.errorCode()) ?? 'Failed to load people.')
        : ''
    ),
    inviteErrorMessage: computed(() =>
      store.inviteStatus() === 'error'
        ? (errorMessages.resolve(store.inviteErrorCode()) ?? 'Could not create the invite.')
        : ''
    ),
    revokeErrorMessage: computed(() => {
      const code = store.revokeErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not revoke access.') : '';
    }),
  };
}
