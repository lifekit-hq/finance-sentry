import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type PushDevice,
  type PushPreferences,
  type PushPublicKey,
  type RegisterPushRequest,
} from '../models/push/push.model';

@Injectable({providedIn: 'root'})
export class PushNotificationsService extends ApiService {
  constructor() {
    super('notifications/push');
  }

  public getPublicKey(): Observable<PushPublicKey> {
    return this.get<PushPublicKey>('public-key');
  }

  public listDevices(): Observable<PushDevice[]> {
    return this.get<PushDevice[]>('subscriptions');
  }

  public register(request: RegisterPushRequest): Observable<PushDevice> {
    return this.post<PushDevice>('subscriptions', request);
  }

  public remove(id: string): Observable<void> {
    return this.delete<void>(`subscriptions/${encodeURIComponent(id)}`);
  }

  public getPreferences(): Observable<PushPreferences> {
    return this.get<PushPreferences>('preferences');
  }

  public setPreferences(pushEnabled: boolean): Observable<PushPreferences> {
    return this.put<PushPreferences>('preferences', {pushEnabled});
  }
}
