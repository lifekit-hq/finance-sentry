import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Invite, type Person} from '../../models/person/person.model';
import {PeopleService} from '../../services/people.service';
import {peopleEffects, peopleHooks} from './people.effects';

const PEOPLE: Person[] = [
  {id: 'u-1', email: 'friend@test.com', roles: ['Member'], status: 'Invited'},
];
const INVITE: Invite = {
  userId: 'u-1',
  email: 'friend@test.com',
  token: 'one-time',
  expiresAt: '2099-01-01T00:00:00Z',
};

function buildStore() {
  return {
    setLoading: vi.fn(),
    setPeople: vi.fn(),
    setLoadError: vi.fn(),
    setInviting: vi.fn(),
    setInvite: vi.fn(),
    setInviteError: vi.fn(),
    setRevoking: vi.fn(),
    setRevoked: vi.fn(),
    setRevokeError: vi.fn(),
  };
}

function apiError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 400, error: {errorCode}}));
}

describe('peopleEffects', () => {
  let service: {
    list: ReturnType<typeof vi.fn>;
    createInvite: ReturnType<typeof vi.fn>;
    revoke: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {
      list: vi.fn().mockReturnValue(of(PEOPLE)),
      createInvite: vi.fn(),
      revoke: vi.fn(),
    };
    TestBed.configureTestingModule({providers: [{provide: PeopleService, useValue: service}]});
  });

  function effects(store: ReturnType<typeof buildStore>) {
    return TestBed.runInInjectionContext(() => peopleEffects(store));
  }

  describe('load', () => {
    it('stores the people list', () => {
      const store = buildStore();

      effects(store).load();

      expect(store.setLoading).toHaveBeenCalled();
      expect(store.setPeople).toHaveBeenCalledWith(PEOPLE);
    });

    it('records the error code on failure', () => {
      const store = buildStore();
      service.list.mockReturnValue(apiError('FORBIDDEN'));

      effects(store).load();

      expect(store.setLoadError).toHaveBeenCalledWith('FORBIDDEN');
    });
  });

  describe('createInvite', () => {
    it('keeps the invite and reloads the list', () => {
      const store = buildStore();
      service.createInvite.mockReturnValue(of(INVITE));

      effects(store).createInvite({email: 'friend@test.com'});

      expect(service.createInvite).toHaveBeenCalledWith({email: 'friend@test.com'});
      expect(store.setInviting).toHaveBeenCalled();
      expect(store.setInvite).toHaveBeenCalledWith(INVITE);
      expect(service.list).toHaveBeenCalled();
    });

    it('records the error code on failure', () => {
      const store = buildStore();
      service.createInvite.mockReturnValue(apiError('DUPLICATE_EMAIL'));

      effects(store).createInvite({email: 'friend@test.com'});

      expect(store.setInviteError).toHaveBeenCalledWith('DUPLICATE_EMAIL');
      expect(store.setInvite).not.toHaveBeenCalled();
    });
  });

  describe('revoke', () => {
    it('revokes and reloads the list', () => {
      const store = buildStore();
      service.revoke.mockReturnValue(of(undefined));

      effects(store).revoke('u-1');

      expect(store.setRevoking).toHaveBeenCalledWith('u-1');
      expect(service.revoke).toHaveBeenCalledWith('u-1');
      expect(store.setRevoked).toHaveBeenCalled();
      expect(service.list).toHaveBeenCalled();
    });

    it('records the error code on failure', () => {
      const store = buildStore();
      service.revoke.mockReturnValue(apiError('CANNOT_REVOKE_OWNER'));

      effects(store).revoke('u-1');

      expect(store.setRevokeError).toHaveBeenCalledWith('CANNOT_REVOKE_OWNER');
      expect(store.setRevoked).not.toHaveBeenCalled();
    });
  });

  it('peopleHooks loads the list on init', () => {
    const store = {load: vi.fn()};

    peopleHooks(store);

    expect(store.load).toHaveBeenCalled();
  });
});
