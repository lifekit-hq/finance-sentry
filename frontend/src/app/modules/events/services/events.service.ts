import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type EventKind,
  type FiredEventsPageResponse,
  type UpcomingEventsResult,
} from '../models/event/event.model';

@Injectable({providedIn: 'root'})
export class EventsService extends ApiService {
  constructor() {
    super('events');
  }

  public getUpcoming(
    from: string,
    to: string,
    kinds: EventKind[]
  ): Observable<UpcomingEventsResult> {
    const params: Record<string, string> = {from, to};
    if (kinds.length > 0) {
      params['kinds'] = kinds.join(',');
    }
    return this.get<UpcomingEventsResult>('upcoming', params);
  }

  public getFired(page: number, pageSize: number): Observable<FiredEventsPageResponse> {
    return this.get<FiredEventsPageResponse>('fired', {page, pageSize});
  }
}
