import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type UpdateProfileRequest, type UserProfile} from '@shared/models/settings/settings.model';
import {type Observable} from 'rxjs';

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
