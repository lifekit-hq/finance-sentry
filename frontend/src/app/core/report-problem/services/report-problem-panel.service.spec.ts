import {TestBed} from '@angular/core/testing';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ReportProblemSheetComponent} from '../components/report-problem-sheet/report-problem-sheet.component';
import {ReportProblemPanelService} from './report-problem-panel.service';

describe('ReportProblemPanelService', () => {
  const open = vi.fn();

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({providers: [{provide: CmnDrawerService, useValue: {open}}]});
  });

  it('opens the sheet in the shared drawer', () => {
    TestBed.inject(ReportProblemPanelService).open();

    expect(open).toHaveBeenCalledWith(
      ReportProblemSheetComponent,
      expect.objectContaining({title: 'Report a problem'})
    );
  });
});
