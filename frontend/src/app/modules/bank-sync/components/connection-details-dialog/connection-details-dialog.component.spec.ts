import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {type ConnectionDetailsDialogData} from '../../models/connection-details/connection-details.model';
import {ConnectionDetailsDialogComponent} from './connection-details-dialog.component';

describe('ConnectionDetailsDialogComponent', () => {
  const data: ConnectionDetailsDialogData = {
    rows: [
      {label: 'Provider', value: 'Monobank'},
      {label: 'Last synced', value: '5 minutes ago'},
    ],
  };

  it('lists each row as a label and its value, in order', () => {
    TestBed.configureTestingModule({providers: [{provide: CMN_DIALOG_DATA, useValue: data}]});
    const fixture = TestBed.createComponent(ConnectionDetailsDialogComponent);
    fixture.detectChanges();

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="connection-detail"]')
    ).map(row => [row.querySelector('dt')?.textContent, row.querySelector('dd')?.textContent]);

    expect(rows).toEqual([
      ['Provider', 'Monobank'],
      ['Last synced', '5 minutes ago'],
    ]);
  });
});
