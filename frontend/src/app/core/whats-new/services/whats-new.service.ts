import {HttpClient} from '@angular/common/http';
import {inject, Injectable} from '@angular/core';
import {type Observable} from 'rxjs';

import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {WHATS_NEW_URL} from '../constants/whats-new.constants';
import {type WhatsNewData} from '../models/whats-new.model';

/** Reads the static `whats-new.json` that ships beside the bundle (not an API endpoint). */
@Injectable({providedIn: 'root'})
export class WhatsNewService {
  private readonly http = inject(HttpClient);

  /** The version in the query keeps a browser-cached copy from an older release out of the panel. */
  public load(): Observable<WhatsNewData> {
    return this.http.get<WhatsNewData>(WHATS_NEW_URL, {params: {v: APP_VERSION}});
  }
}
