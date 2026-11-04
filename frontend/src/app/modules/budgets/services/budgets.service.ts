import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type Budget,
  type BudgetSummaryResponse,
  type CreateBudgetRequest,
  type UpdateBudgetRequest,
} from '../models/budget/budget.model';

@Injectable({providedIn: 'root'})
export class BudgetsService extends ApiService {
  constructor() {
    super('budgets');
  }

  public createBudget(req: CreateBudgetRequest): Observable<Budget> {
    return this.post<Budget>('', req);
  }

  public updateBudget(id: string, req: UpdateBudgetRequest): Observable<Budget> {
    return this.put<Budget>(id, req);
  }

  public deleteBudget(id: string): Observable<void> {
    return this.delete<void>(id);
  }

  public getBudgetSummary(year?: number, month?: number): Observable<BudgetSummaryResponse> {
    return this.get<BudgetSummaryResponse>('summary', {year, month});
  }
}
