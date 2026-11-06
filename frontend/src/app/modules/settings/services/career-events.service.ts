import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type CareerEvent, type CareerEventInput} from '../models/career-event/career-event.model';

@Injectable({providedIn: 'root'})
export class CareerEventsService extends ApiService {
  constructor() {
    super('wealth/career-events');
  }

  public list(): Observable<CareerEvent[]> {
    return this.get<CareerEvent[]>();
  }

  public create(input: CareerEventInput): Observable<CareerEvent> {
    return this.post<CareerEvent>('', input);
  }

  public update(id: string, input: CareerEventInput): Observable<CareerEvent> {
    return this.put<CareerEvent>(id, input);
  }

  public retire(id: string): Observable<CareerEvent> {
    return this.post<CareerEvent>(`${id}/retire`);
  }

  public remove(id: string): Observable<void> {
    return this.delete<void>(id);
  }
}
