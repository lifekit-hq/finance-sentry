import {inject, Injectable} from '@angular/core';
import {CmnDrawerService} from '@lifekit-hq/ui';

import {ReportProblemSheetComponent} from '../components/report-problem-sheet/report-problem-sheet.component';
import {
  REPORT_PROBLEM_DRAWER_WIDTH,
  REPORT_PROBLEM_LABEL,
} from '../constants/report-problem.constants';

/** The one way into the sheet (More page, avatar menu, palette): a side panel from 768px, a bottom sheet below. */
@Injectable({providedIn: 'root'})
export class ReportProblemPanelService {
  private readonly drawer = inject(CmnDrawerService);

  public open(): void {
    this.drawer.open(ReportProblemSheetComponent, {
      title: REPORT_PROBLEM_LABEL,
      width: REPORT_PROBLEM_DRAWER_WIDTH,
    });
  }
}
