import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type IncomeSource,
  type IncomeSourceInput,
} from '../models/income-source/income-source.model';

@Injectable({providedIn: 'root'})
export class IncomeSourcesService extends ApiService {
  constructor() {
    super('wealth/income-sources');
  }

  public list(): Observable<IncomeSource[]> {
    return this.get<IncomeSource[]>();
  }

  public create(input: IncomeSourceInput): Observable<IncomeSource> {
    return this.post<IncomeSource>('', input);
  }

  public update(id: string, input: IncomeSourceInput): Observable<IncomeSource> {
    return this.put<IncomeSource>(id, input);
  }

  public retire(id: string): Observable<IncomeSource> {
    return this.post<IncomeSource>(`${id}/retire`);
  }

  public remove(id: string): Observable<void> {
    return this.delete<void>(id);
  }
}
