import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {AuthStore} from '../../../auth/store/auth.store';
import {type Invite, type Person} from '../../models/person/person.model';
import {PeopleService} from '../../services/people.service';
import {PeopleStore} from './people.store';

const PEOPLE: Person[] = [
  {id: 'u-1', email: 'friend@test.com', roles: ['Member'], status: 'Invited'},
];
const INVITE: Invite = {userId: 'u-1', email: 'friend@test.com'};

function apiError(errorCode?: string) {
  return throwError(
    () => new HttpErrorResponse({status: 400, error: errorCode ? {errorCode} : null})
  );
}

describe('PeopleStore', () => {
  let service: {
    list: ReturnType<typeof vi.fn>;
    createInvite: ReturnType<typeof vi.fn>;
    revoke: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {list: vi.fn(), createInvite: vi.fn(), revoke: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        PeopleStore,
        {provide: PeopleService, useValue: service},
        {provide: AuthStore, useValue: {userId: signal('owner')}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
  });

  it('loads the list on init and reports it as loading until the answer arrives', () => {
    service.list.mockReturnValue(NEVER);

    const store = TestBed.inject(PeopleStore);

    expect(store.peopleStatus()).toBe('loading');
    expect(store.isLoading()).toBe(true);
    expect(store.isPeopleLoading()).toBe(true);
  });

  it('stores the people and settles on success', () => {
    service.list.mockReturnValue(of(PEOPLE));

    const store = TestBed.inject(PeopleStore);

    expect(store.people()).toEqual(PEOPLE);
    expect(store.peopleStatus()).toBe('success');
    expect(store.isLoading()).toBe(false);
    expect(store.peopleErrorMessage()).toBe('');
  });

  it('resolves a known load error code through the registry', () => {
    service.list.mockReturnValue(apiError('FORBIDDEN'));

    const store = TestBed.inject(PeopleStore);

    expect(store.hasPeopleError()).toBe(true);
    expect(store.peopleErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['FORBIDDEN']);
  });

  it('falls back to the people default for an unknown load error', () => {
    service.list.mockReturnValue(apiError());

    const store = TestBed.inject(PeopleStore);

    expect(store.peopleErrorMessage()).toBe('Failed to load people.');
  });

  it('tracks the invite apart from the list', () => {
    service.list.mockReturnValue(of(PEOPLE));
    service.createInvite.mockReturnValue(NEVER);
    const store = TestBed.inject(PeopleStore);

    store.createInvite({email: 'friend@test.com'});

    expect(store.isInviteLoading()).toBe(true);
    expect(store.isPeopleLoading()).toBe(false);
    expect(store.lastInvite()).toBeNull();
  });

  it('keeps the invite once created and settles the invite status', () => {
    service.list.mockReturnValue(of(PEOPLE));
    service.createInvite.mockReturnValue(of(INVITE));
    const store = TestBed.inject(PeopleStore);

    store.createInvite({email: 'friend@test.com'});

    expect(store.lastInvite()).toEqual(INVITE);
    expect(store.inviteStatus()).toBe('success');
    expect(store.inviteErrorMessage()).toBe('');
  });

  it('resolves an invite error code and falls back to the invite default', () => {
    service.list.mockReturnValue(of(PEOPLE));
    service.createInvite.mockReturnValue(apiError('DUPLICATE_EMAIL'));
    const store = TestBed.inject(PeopleStore);

    store.createInvite({email: 'friend@test.com'});
    expect(store.inviteErrorMessage()).toBe('This email already belongs to an account.');

    service.createInvite.mockReturnValue(apiError());
    store.createInvite({email: 'friend@test.com'});
    expect(store.inviteErrorMessage()).toBe('Could not create the invite.');
    expect(store.peopleStatus()).toBe('success');
  });
});
