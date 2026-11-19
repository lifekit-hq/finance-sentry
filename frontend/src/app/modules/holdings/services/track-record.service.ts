import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type TrackRecord} from '../models/book-performance/track-record.model';

@Injectable({providedIn: 'root'})
export class TrackRecordService extends ApiService {
  constructor() {
    super('');
  }

  public getTrackRecord(): Observable<TrackRecord> {
    return this.get<TrackRecord>('research/theses/track-record');
  }
}
