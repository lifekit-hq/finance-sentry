import {DialogRef} from '@angular/cdk/dialog';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA, CMN_DIALOG_PRESENTATION} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type DisconnectDialogData} from '../../models/disconnect-dialog/disconnect-dialog.model';
import {DisconnectDialogComponent} from './disconnect-dialog.component';

describe('DisconnectDialogComponent', () => {
  const data: DisconnectDialogData = {providerName: 'Monobank'};
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

  it('exposes the injected provider name on data', () => {
    const fixture = TestBed.createComponent(DisconnectDialogComponent);
    expect(fixture.componentInstance.data).toEqual(data);
  });

  it('confirm() closes with true', () => {
    const fixture = TestBed.createComponent(DisconnectDialogComponent);
    fixture.componentInstance.confirm();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('cancel() closes with false', () => {
    const fixture = TestBed.createComponent(DisconnectDialogComponent);
    fixture.componentInstance.cancel();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('renders the provider name in the dialog body', () => {
    const fixture = TestBed.createComponent(DisconnectDialogComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Monobank');
  });

  describe('cancel button', () => {
    const buttonLabels = (presentation?: 'dialog' | 'action-sheet'): string[] => {
      if (presentation) {
        TestBed.configureTestingModule({
          providers: [{provide: CMN_DIALOG_PRESENTATION, useValue: signal(presentation)}],
        });
      }
      const fixture = TestBed.createComponent(DisconnectDialogComponent);
      fixture.detectChanges();
      const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('cmn-button');
      return Array.from(buttons).map(button => button.textContent?.trim() ?? '');
    };

    it('is rendered when no presentation token is provided', () => {
      expect(buttonLabels()).toEqual(['Cancel', 'Disconnect']);
    });

    it('is rendered when the presentation is a regular dialog', () => {
      expect(buttonLabels('dialog')).toEqual(['Cancel', 'Disconnect']);
    });

    it('is dropped when the presentation is an action sheet', () => {
      expect(buttonLabels('action-sheet')).toEqual(['Disconnect']);
    });
  });
});
