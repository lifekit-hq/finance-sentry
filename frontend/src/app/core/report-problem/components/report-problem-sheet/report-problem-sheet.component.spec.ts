import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {CmnDrawerRef, CmnShellService, ToastService} from '@lifekit-hq/ui';
import {Subject} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {APP_VERSION} from '../../../../shared/constants/version/version.constants';
import {ERROR_MESSAGES_REGISTRY} from '../../../errors/error-messages.registry';
import {REPORT_PROBLEM_FAILED_MESSAGE} from '../../constants/report-problem.constants';
import {
  type ProblemReportAccepted,
  type ProblemReportRequest,
} from '../../models/report-problem.model';
import {ReportProblemService} from '../../services/report-problem.service';
import {ReportProblemSheetComponent} from './report-problem-sheet.component';

describe('ReportProblemSheetComponent', () => {
  const close = vi.fn();
  const success = vi.fn();
  const submit = vi.fn<(request: ProblemReportRequest) => Subject<ProblemReportAccepted>>();
  let response: Subject<ProblemReportAccepted>;

  const render = async (isPhone = false) => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{path: 'accounts', children: [{path: ':id', children: []}]}]),
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {provide: ReportProblemService, useValue: {submit}},
        {provide: CmnDrawerRef, useValue: {close}},
        {provide: ToastService, useValue: {success}},
        {provide: CmnShellService, useValue: {isPhone: signal(isPhone)}},
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/accounts/9f1c?q=lunch#top');
    const fixture = TestBed.createComponent(ReportProblemSheetComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const textarea = host.querySelector<HTMLTextAreaElement>('textarea');
    const send = host.querySelector<HTMLButtonElement>('[data-testid="report-send"] button');
    const type = (value: string): void => {
      if (textarea) {
        textarea.value = value;
        textarea.dispatchEvent(new Event('input'));
      }
      fixture.detectChanges();
    };
    const sendButton = (): HTMLButtonElement | null =>
      host.querySelector<HTMLButtonElement>('[data-testid="report-send"] button');
    return {fixture, host, type, send, sendButton};
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    close.mockClear();
    success.mockClear();
    submit.mockReset();
    response = new Subject<ProblemReportAccepted>();
    submit.mockReturnValue(response);
  });

  it('keeps send disabled until there is a kind or some text', async () => {
    const {host, type, sendButton, fixture} = await render();
    expect(sendButton()?.disabled).toBe(true);

    type('The balance never refreshes');
    expect(sendButton()?.disabled).toBe(false);

    type('   ');
    expect(sendButton()?.disabled).toBe(true);

    host.querySelector<HTMLElement>('[data-testid="report-kind-Idea"] button')?.click();
    fixture.detectChanges();
    expect(sendButton()?.disabled).toBe(false);
  });

  it('sends exactly the client part: route pattern, version, device, families, kind and text', async () => {
    const {host, type, fixture} = await render(true);
    host.querySelector<HTMLElement>('[data-testid="report-kind-LooksWrong"] button')?.click();
    type('  Chart is cut off  ');
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(submit).toHaveBeenCalledOnce();
    const request = submit.mock.calls[0][0];
    expect(Object.keys(request).sort()).toEqual(
      ['appVersion', 'browser', 'device', 'kind', 'os', 'route', 'text'].sort()
    );
    expect(request).toMatchObject({
      kind: 'LooksWrong',
      text: 'Chart is cut off',
      route: '/accounts/:id',
      appVersion: APP_VERSION,
      device: 'Phone',
    });
    expect(request.route).not.toMatch(/9f1c|lunch|top/);
  });

  it('shows the reference in a toast and closes on 202', async () => {
    const {host, type, fixture} = await render();
    type('broken');
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    response.next({reference: 'FS-R-42'});
    fixture.detectChanges();

    expect(success).toHaveBeenCalledWith(expect.stringContaining('FS-R-42'));
    expect(close).toHaveBeenCalledOnce();
  });

  it('disables send while the request is in flight', async () => {
    const {host, type, fixture, sendButton} = await render();
    type('broken');
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(sendButton()?.disabled).toBe(true);
    expect(close).not.toHaveBeenCalled();
  });

  it.each([
    ['REPORT_RATE_LIMITED', 429],
    ['REPORT_NOT_ALLOWED', 403],
  ])('shows the registry message for %s and keeps the text', async (errorCode, status) => {
    const {host, type, fixture} = await render();
    type('broken');
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    response.error(new HttpErrorResponse({status, error: {errorCode}}));
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="report-error"]')?.textContent).toContain(
      ERROR_MESSAGES_REGISTRY[errorCode]
    );
    expect(host.querySelector<HTMLTextAreaElement>('textarea')?.value).toBe('broken');
    expect(close).not.toHaveBeenCalled();
  });

  it('falls back to the generic message when the error has no known code', async () => {
    const {host, type, fixture} = await render();
    type('broken');
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    response.error(new HttpErrorResponse({status: 429}));
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="report-error"]')?.textContent).toContain(
      REPORT_PROBLEM_FAILED_MESSAGE
    );
    expect(host.querySelector<HTMLTextAreaElement>('textarea')?.value).toBe('broken');
    expect(close).not.toHaveBeenCalled();
  });

  it('keeps the text and lets the person retry after another error', async () => {
    const {host, type, fixture, sendButton} = await render();
    type('broken');
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    response.error(new HttpErrorResponse({status: 500}));
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="report-error"]')?.textContent).toContain(
      REPORT_PROBLEM_FAILED_MESSAGE
    );
    expect(host.querySelector<HTMLTextAreaElement>('textarea')?.value).toBe('broken');
    expect(sendButton()?.disabled).toBe(false);
    expect(close).not.toHaveBeenCalled();

    response = new Subject<ProblemReportAccepted>();
    submit.mockReturnValue(response);
    host.querySelector('form')?.dispatchEvent(new Event('submit'));
    response.next({reference: 'FS-R-43'});
    expect(submit).toHaveBeenCalledTimes(2);
    expect(close).toHaveBeenCalledOnce();
  });
});
