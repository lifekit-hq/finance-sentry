import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {Observable} from 'rxjs';

import {AcceptInviteRequest, AuthRequest, AuthResponse} from '../models/auth/auth.model';

@Injectable({providedIn: 'root'})
export class AuthService extends ApiService {
  constructor() {
    super('auth');
  }

  public getMe(): Observable<AuthResponse> {
    return this.get<AuthResponse>('me');
  }

  public login(req: AuthRequest): Observable<AuthResponse> {
    return this.post<AuthResponse>('login', req);
  }

  public acceptInvite(req: AcceptInviteRequest): Observable<AuthResponse> {
    return this.post<AuthResponse>('invite/accept', req);
  }

  public refresh(): Observable<AuthResponse> {
    return this.post<AuthResponse>('refresh');
  }

  public verifyGoogleCredential(credential: string): Observable<AuthResponse> {
    return this.post<AuthResponse>('google/verify', {credential});
  }

  public logout(): Observable<unknown> {
    return this.post<unknown>('logout');
  }
}
