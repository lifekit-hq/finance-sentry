import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type AlertFilter,
  type AlertsPageResponse,
  type UnreadCountResponse,
} from '../models/alert/alert.model';

@Injectable({providedIn: 'root'})
export class AlertsService extends ApiService {
  constructor() {
    super('alerts');
  }

  public getAlerts(
    filter: AlertFilter = 'all',
    page = 1,
    pageSize = 20,
    search = ''
  ): Observable<AlertsPageResponse> {
    return this.get<AlertsPageResponse>('', {
      filter,
      page,
      pageSize,
      search: search.trim() || undefined,
    });
  }

  public getUnreadCount(): Observable<UnreadCountResponse> {
    return this.get<UnreadCountResponse>('unread-count');
  }

  public markRead(id: string): Observable<void> {
    return this.patch<void>(`${id}/read`);
  }

  public markAllRead(): Observable<void> {
    return this.patch<void>('read-all');
  }

  public dismiss(id: string): Observable<void> {
    return this.delete<void>(id);
  }
}
