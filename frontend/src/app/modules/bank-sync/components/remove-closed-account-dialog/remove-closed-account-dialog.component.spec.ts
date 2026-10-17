import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type RemoveClosedAccountDialogData} from '../../models/remove-closed-account-dialog/remove-closed-account-dialog.model';
import {RemoveClosedAccountDialogComponent} from './remove-closed-account-dialog.component';

describe('RemoveClosedAccountDialogComponent', () => {
  const data: RemoveClosedAccountDialogData = {accountLabel: 'Monobank •••• 9312'};
  let dialogRef: {close: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    dialogRef = {close: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: DialogRef, useValue: dialogRef},
        {provide: CMN_DIALOG_DATA, useValue: data},
      ],
    });
  });

  it('confirm() closes with true', () => {
    TestBed.createComponent(RemoveClosedAccountDialogComponent).componentInstance.confirm();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('cancel() closes with false', () => {
    TestBed.createComponent(RemoveClosedAccountDialogComponent).componentInstance.cancel();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('names the account and says its history goes with it', () => {
    const fixture = TestBed.createComponent(RemoveClosedAccountDialogComponent);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Monobank •••• 9312');
    expect(text).toContain('transaction history will be removed');
  });
});
