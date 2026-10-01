import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type CreateInviteRequest, type Invite, type Person} from '../models/person/person.model';

@Injectable({providedIn: 'root'})
export class PeopleService extends ApiService {
  constructor() {
    super('people');
  }

  public list(): Observable<Person[]> {
    return this.get<Person[]>();
  }

  public createInvite(request: CreateInviteRequest): Observable<Invite> {
    return this.post<Invite>('invites', request);
  }

  public revoke(userId: string): Observable<void> {
    return this.post<void>(`${encodeURIComponent(userId)}/revoke`);
  }
}
