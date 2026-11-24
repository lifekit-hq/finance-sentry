import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type BookIncome} from '../models/book-income/book-income.model';

@Injectable({providedIn: 'root'})
export class BookIncomeService extends ApiService {
  constructor() {
    super('');
  }

  public getIncome(): Observable<BookIncome> {
    return this.get<BookIncome>('radar/book-performance/income');
  }
}
