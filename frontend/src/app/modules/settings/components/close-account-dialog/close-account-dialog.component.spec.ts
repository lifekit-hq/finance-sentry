import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type CloseAccountDialogData} from '../../models/account-closure/account-closure.model';
import {CloseAccountDialogComponent} from './close-account-dialog.component';

describe('CloseAccountDialogComponent', () => {
  let dialogRef: {close: ReturnType<typeof vi.fn>};

  const create = (data: CloseAccountDialogData) => {
    TestBed.configureTestingModule({
      providers: [
        {provide: DialogRef, useValue: dialogRef},
        {provide: CMN_DIALOG_DATA, useValue: data},
      ],
    });
    const fixture = TestBed.createComponent(CloseAccountDialogComponent);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    dialogRef = {close: vi.fn()};
  });

  it('does not close while the typed text is not the account email', () => {
    const fixture = create({email: 'ann@example.com', purgeDays: 30});
    fixture.componentInstance.control.setValue('ann@example');
    fixture.componentInstance.close();
    expect(fixture.componentInstance.confirmed()).toBe(false);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with the typed email once it matches', () => {
    const fixture = create({email: 'ann@example.com', purgeDays: 30});
    fixture.componentInstance.control.setValue(' Ann@Example.com ');
    fixture.componentInstance.close();
    expect(dialogRef.close).toHaveBeenCalledWith('Ann@Example.com');
  });

  it('submitting the form closes with the typed email, without a native page submit', () => {
    const fixture = create({email: 'ann@example.com', purgeDays: 30});
    fixture.componentInstance.control.setValue('ann@example.com');
    fixture.detectChanges();
    const form = (fixture.nativeElement as HTMLElement).querySelector('form') as HTMLFormElement;
    const submit = new Event('submit', {cancelable: true});
    form.dispatchEvent(submit);
    expect(submit.defaultPrevented).toBe(true);
    expect(dialogRef.close).toHaveBeenCalledWith('ann@example.com');
  });

  it('cancel() closes with nothing', () => {
    const fixture = create({email: 'ann@example.com', purgeDays: 30});
    fixture.componentInstance.cancel();
    expect(dialogRef.close).toHaveBeenCalledWith();
  });

  it('states the purge delay when the data is kept for a grace period', () => {
    const fixture = create({email: 'ann@example.com', purgeDays: 30});
    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('30 days');
    expect(text).toContain('Type ann@example.com to confirm');
  });
});
