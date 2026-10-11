import {HttpContext} from '@angular/common/http';
import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {Observable} from 'rxjs';

import {SESSION_PROBE, SKIP_AUTH_REFRESH} from '../constants/auth/auth-http-context.constants';
import {
  AcceptInviteRequest,
  AuthRequest,
  AuthResponse,
  LogoutResponse,
  SignInMethods,
} from '../models/auth/auth.model';

const OWN_CALL = new HttpContext().set(SKIP_AUTH_REFRESH, true);
const SESSION_PROBE_CALL = new HttpContext().set(SKIP_AUTH_REFRESH, true).set(SESSION_PROBE, true);

@Injectable({providedIn: 'root'})
export class AuthService extends ApiService {
  constructor() {
    super('auth');
  }

  public getMe(): Observable<AuthResponse> {
    return this.http.get<AuthResponse>(`${this.baseUrl}/me`, {context: SESSION_PROBE_CALL});
  }

  public getSignInMethods(): Observable<SignInMethods> {
    return this.http.get<SignInMethods>(`${this.baseUrl}/methods`, {context: OWN_CALL});
  }

  public login(req: AuthRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/login`, req, {context: OWN_CALL});
  }

  public acceptInvite(req: AcceptInviteRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/invite/accept`, req, {context: OWN_CALL});
  }

  public refresh(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/refresh`, null, {context: OWN_CALL});
  }

  public logout(): Observable<Nullable<LogoutResponse>> {
    return this.http.post<Nullable<LogoutResponse>>(`${this.baseUrl}/logout`, null, {
      context: OWN_CALL,
    });
  }
}
