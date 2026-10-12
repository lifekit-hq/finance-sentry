import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ProblemReportAccepted,
  type ProblemReportRequest,
} from '../models/report-problem.model';

@Injectable({providedIn: 'root'})
export class ReportProblemService extends ApiService {
  constructor() {
    super('feedback');
  }

  public submit(request: ProblemReportRequest): Observable<ProblemReportAccepted> {
    return this.post<ProblemReportAccepted>('', request);
  }
}
