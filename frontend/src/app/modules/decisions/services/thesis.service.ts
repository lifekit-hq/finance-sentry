import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type ThesisDto} from '../../../shared/models/thesis/thesis.model';

/** The owner's theses, read so a revision card can show what a proposal would change them from. */
@Injectable({providedIn: 'root'})
export class ThesisService extends ApiService {
  constructor() {
    super('research/theses');
  }

  public list(): Observable<ThesisDto[]> {
    return this.get<ThesisDto[]>('');
  }
}
