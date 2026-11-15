import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA, CmnDrawerRef, ToastService} from '@lifekit-hq/ui';
import {Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {ThesisRevisionFixtures} from '../../../../shared/models/thesis-revision/thesis-revision.fixtures';
import {
  type DecideThesisRevisionResult,
  type ThesisRevisionVerdict,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {
  THESIS_REVISION_FAILED_MESSAGE,
  THESIS_REVISION_WALL_TEXT,
} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisRevisionService} from '../../services/thesis-revision.service';
import {ThesisRevisionSheetComponent} from './thesis-revision-sheet.component';

const revision = ThesisRevisionFixtures.revision();

function decided(overrides: Partial<DecideThesisRevisionResult> = {}): DecideThesisRevisionResult {
  return {
    revision: {...revision, status: 'Confirmed'},
    thesis: ThesisRevisionFixtures.thesis(),
    gate: null,
    note: null,
    ...overrides,
  };
}

describe('ThesisRevisionSheetComponent', () => {
  const close = vi.fn();
  const success = vi.fn();
  let response: Subject<DecideThesisRevisionResult>;
  let service: {confirm: ReturnType<typeof vi.fn>; reject: ReturnType<typeof vi.fn>};

  const render = (verdict: ThesisRevisionVerdict) => {
    TestBed.configureTestingModule({
      providers: [
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {provide: ThesisRevisionService, useValue: service},
        {provide: CmnDrawerRef, useValue: {close}},
        {provide: CMN_DRAWER_DATA, useValue: {revision, verdict}},
        {provide: ToastService, useValue: {success}},
      ],
    });
    const fixture = TestBed.createComponent(ThesisRevisionSheetComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const type = (value: string): void => {
      const textarea = host.querySelector<HTMLTextAreaElement>('textarea');
      if (textarea) {
        textarea.value = value;
        textarea.dispatchEvent(new Event('input'));
      }
      fixture.detectChanges();
    };
    const submit = (): void => {
      host.querySelector('form')?.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    const sendButton = (): HTMLButtonElement | null =>
      host.querySelector<HTMLButtonElement>('[data-testid="thesis-revision-submit"] button');
    return {fixture, host, type, submit, sendButton};
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    close.mockClear();
    success.mockClear();
    response = new Subject<DecideThesisRevisionResult>();
    service = {
      confirm: vi.fn().mockReturnValue(response),
      reject: vi.fn().mockReturnValue(response),
    };
  });

  it('says on confirm that the thesis is written and no position is touched', () => {
    const {host} = render('confirm');

    expect(host.querySelector('[data-testid="thesis-revision-sheet-wall"]')?.textContent).toContain(
      THESIS_REVISION_WALL_TEXT
    );
    expect(host.querySelector('[data-testid="thesis-revision-reason"]')).toBeNull();
    expect(host.querySelector('[data-testid="thesis-revision-override"]')).toBeNull();
  });

  it('confirms with an optional trimmed note, then toasts and closes with the decision', () => {
    const {type, submit, sendButton} = render('confirm');
    expect(sendButton()?.disabled).toBe(false);

    type('  Agree on the anchors  ');
    submit();
    expect(service.confirm).toHaveBeenCalledWith('rev-1', {
      note: 'Agree on the anchors',
      overrideRisk: false,
    });
    expect(sendButton()?.disabled).toBe(true);

    const result = decided();
    response.next(result);
    expect(success).toHaveBeenCalledWith('Thesis updated. (CBRS)');
    expect(close).toHaveBeenCalledWith(result);
  });

  it('needs a reason to reject and sends it trimmed', () => {
    const {type, submit, sendButton, host} = render('reject');
    expect(host.querySelector('[data-testid="thesis-revision-sheet-wall"]')).toBeNull();
    expect(sendButton()?.disabled).toBe(true);

    submit();
    expect(service.reject).not.toHaveBeenCalled();

    type('   ');
    expect(sendButton()?.disabled).toBe(true);

    type('  The S-1 changes nothing  ');
    expect(sendButton()?.disabled).toBe(false);
    submit();
    expect(service.reject).toHaveBeenCalledWith('rev-1', {reason: 'The S-1 changes nothing'});

    response.next(decided({revision: {...revision, status: 'Rejected'}, thesis: null}));
    expect(success).toHaveBeenCalledWith('Rejection recorded. (CBRS)');
    expect(close).toHaveBeenCalledOnce();
  });

  it('resolves a registered error and keeps the sheet open', () => {
    service.confirm.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({status: 409, error: {errorCode: 'THESIS_REVISION_NOT_PENDING'}})
      )
    );
    const {submit, host, sendButton} = render('confirm');

    submit();

    expect(host.querySelector('[data-testid="thesis-revision-error"]')?.textContent).toContain(
      ERROR_MESSAGES_REGISTRY['THESIS_REVISION_NOT_PENDING']
    );
    expect(close).not.toHaveBeenCalled();
    expect(sendButton()?.disabled).toBe(false);
  });

  it('falls back to the default message for an unregistered failure', () => {
    service.confirm.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));
    const {submit, host} = render('confirm');

    submit();

    expect(host.querySelector('[data-testid="thesis-revision-error"]')?.textContent).toContain(
      THESIS_REVISION_FAILED_MESSAGE
    );
  });

  it('shows the risk gate refusal, keeps the proposal pending and offers the explicit override', () => {
    const gate = {
      ruleKey: 'single_name_cap',
      observedValue: 0.14,
      limitValue: 0.1,
      maxCompliantSizeUsd: 1200,
      note: 'Above the single-name cap.',
    };
    service.confirm.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {revision, thesis: null, gate, note: null},
          })
      )
    );
    const {submit, host, fixture, sendButton} = render('confirm');

    submit();

    const refusal = host.querySelector('[data-testid="thesis-revision-refusal"]');
    expect(refusal?.textContent).toContain('Above the single-name cap.');
    expect(refusal?.textContent).toContain('$1,200.00');
    expect(host.querySelector('[data-testid="thesis-revision-error"]')).toBeNull();
    expect(close).not.toHaveBeenCalled();
    expect(sendButton()?.disabled).toBe(false);

    host
      .querySelector<HTMLElement>('[data-testid="thesis-revision-override"] [role="checkbox"]')
      ?.click();
    fixture.detectChanges();
    submit();

    expect(service.confirm).toHaveBeenLastCalledWith('rev-1', {note: null, overrideRisk: true});
  });

  it('does not read a 409 without a gate as a refusal', () => {
    service.confirm.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({status: 409, error: {errorCode: 'THESIS_REVISION_NOT_PENDING'}})
      )
    );
    const {submit, host} = render('confirm');

    submit();

    expect(host.querySelector('[data-testid="thesis-revision-refusal"]')).toBeNull();
    expect(host.querySelector('[data-testid="thesis-revision-error"]')).not.toBeNull();
  });
});
