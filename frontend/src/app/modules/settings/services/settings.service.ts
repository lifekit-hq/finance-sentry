import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type UpdateProfileRequest, type UserProfile} from '../models/settings/settings.model';

@Injectable({providedIn: 'root'})
export class SettingsService extends ApiService {
  constructor() {
    super('profile');
  }

  public getProfile(): Observable<UserProfile> {
    return this.get<UserProfile>();
  }

  public updateProfile(request: UpdateProfileRequest): Observable<UserProfile> {
    return this.put<UserProfile>('', request);
  }
}
