import {TestBed} from '@angular/core/testing';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Headline} from '../../models/headline/headline.model';
import {HeadlineReasonSheetComponent} from '../headline-reason-sheet/headline-reason-sheet.component';
import {HeadlineReasonSheetService} from './headline-reason-sheet.service';

describe('HeadlineReasonSheetService', () => {
  const open = vi.fn();

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({providers: [{provide: CmnDrawerService, useValue: {open}}]});
  });

  it('opens the sheet in the drawer with the headline and badge as data', () => {
    const headline = {quality: 'ESTIMATED', reasons: []} as unknown as Headline;
    const badge = {kind: 'estimated', label: 'Estimated'} as const;
    TestBed.inject(HeadlineReasonSheetService).open(headline, badge, 'Net worth');
    expect(open).toHaveBeenCalledWith(
      HeadlineReasonSheetComponent,
      expect.objectContaining({title: 'Net worth', data: {headline, badge}})
    );
  });
});
