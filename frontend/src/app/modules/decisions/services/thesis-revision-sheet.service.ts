import {inject, Injectable} from '@angular/core';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {type Observable} from 'rxjs';

import {
  type DecideThesisRevisionResult,
  type ThesisRevisionDto,
  type ThesisRevisionSheetData,
  type ThesisRevisionVerdict,
} from '../../../shared/models/thesis-revision/thesis-revision.model';
import {ThesisRevisionSheetComponent} from '../components/thesis-revision-sheet/thesis-revision-sheet.component';
import {THESIS_REVISION_SHEET_WIDTH} from '../constants/thesis-revision/thesis-revision.constants';

const SHEET_TITLE: Readonly<Record<ThesisRevisionVerdict, string>> = {
  confirm: 'Confirm thesis change',
  reject: 'Reject thesis change',
};

/** The one way into the Confirm and Reject sheets for a thesis revision: a side panel from 768px, a bottom sheet below. */
@Injectable({providedIn: 'root'})
export class ThesisRevisionSheetService {
  private readonly drawer = inject(CmnDrawerService);

  /** Emits the recorded decision, or undefined when the sheet is dismissed. */
  public open(
    revision: ThesisRevisionDto,
    verdict: ThesisRevisionVerdict
  ): Observable<DecideThesisRevisionResult | undefined> {
    return this.drawer
      .open<DecideThesisRevisionResult, ThesisRevisionSheetData>(ThesisRevisionSheetComponent, {
        title: SHEET_TITLE[verdict],
        width: THESIS_REVISION_SHEET_WIDTH,
        data: {revision, verdict},
      })
      .afterClosed();
  }
}
