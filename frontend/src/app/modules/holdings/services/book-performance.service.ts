import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type BookPerformance} from '../models/book-performance/book-performance.model';

@Injectable({providedIn: 'root'})
export class BookPerformanceService extends ApiService {
  constructor() {
    super('');
  }

  public getPerformance(): Observable<BookPerformance> {
    return this.get<BookPerformance>('radar/book-performance');
  }
}
