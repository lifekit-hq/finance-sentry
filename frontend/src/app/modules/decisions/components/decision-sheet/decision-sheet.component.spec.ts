import {Clipboard} from '@angular/cdk/clipboard';
import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA, CmnDrawerRef, ToastService} from '@lifekit-hq/ui';
import {Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../../shared/models/recommendation/recommendation.model';
import {
  DECISION_FAILED_MESSAGE,
  HOLD_NO_ORDER_TEXT,
  NO_TRADE_WALL_TEXT,
  NO_TRADE_WALL_TITLE,
  ORDER_COPIED_MESSAGE,
} from '../../constants/recommendation/recommendation.constants';
import {RecommendationService} from '../../services/recommendation.service';
import {DecisionSheetComponent} from './decision-sheet.component';

function mkRecommendation(overrides: Partial<RecommendationDto> = {}): RecommendationDto {
  return {
    id: 'rec-1',
    ticker: 'GRAB',
    action: 'Trim',
    status: 'Pending',
    createdBy: 'Ledger',
    origin: 'Ledger',
    triggerKind: 'RiskRule',
    triggerRef: 'concentration',
    rationale: 'Above the single-name cap.',
    confidence: 'Medium',
    invalidationTrigger: 'Back under the cap.',
    evidence: [],
    sizeUsd: 2080,
    sizeShares: 660,
    shareOfBookBeforePct: 12.2,
    shareOfBookAfterPct: 9.5,
    limitPrice: 3.13,
    limitCurrency: 'USD',
    venue: 'IBKR',
    orderText: 'SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR',
    createdAt: '2026-10-12T09:00:00Z',
    expiresAt: '2026-10-19T09:00:00Z',
    decidedAt: null,
    decisionNote: null,
    execution: 'Pending',
    executedAt: null,
    subjectReturnPct: null,
    benchmarkReturnPct: null,
    excessReturnPct: null,
    decisionValuePct: null,
    outcomeScoredAt: null,
    ...overrides,
  };
}

describe('DecisionSheetComponent', () => {
  const close = vi.fn();
  const success = vi.fn();
  const copy = vi.fn();
  let response: Subject<RecommendationDto>;
  let service: {confirm: ReturnType<typeof vi.fn>; reject: ReturnType<typeof vi.fn>};

  const render = (verdict: RecommendationVerdict, recommendation = mkRecommendation()) => {
    TestBed.configureTestingModule({
      providers: [
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {provide: RecommendationService, useValue: service},
        {provide: CmnDrawerRef, useValue: {close}},
        {provide: CMN_DRAWER_DATA, useValue: {recommendation, verdict}},
        {provide: ToastService, useValue: {success}},
        {provide: Clipboard, useValue: {copy}},
      ],
    });
    const fixture = TestBed.createComponent(DecisionSheetComponent);
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
      host.querySelector<HTMLButtonElement>('[data-testid="decision-submit"] button');
    return {fixture, host, type, submit, sendButton};
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    close.mockClear();
    success.mockClear();
    copy.mockReset();
    response = new Subject<RecommendationDto>();
    service = {
      confirm: vi.fn().mockReturnValue(response),
      reject: vi.fn().mockReturnValue(response),
    };
  });

  it('shows the exact order and the no-trade wall on confirm', () => {
    const {host} = render('confirm');

    const order = host.querySelector('[data-testid="decision-order-text"]')?.textContent ?? '';
    expect(order).toContain('SELL 660 GRAB');
    expect(order).toContain('LIMIT 3.13 USD DAY');
    expect(order).toContain('IBKR');
    const wall = host.querySelector('[data-testid="decision-no-trade-wall"]')?.textContent ?? '';
    expect(wall).toContain(NO_TRADE_WALL_TITLE);
    expect(wall).toContain(NO_TRADE_WALL_TEXT);
    expect(host.querySelector('[data-testid="decision-reason"]')).toBeNull();
  });

  it('says no order is needed for a hold', () => {
    const {host} = render('confirm', mkRecommendation({action: 'Hold', orderText: null}));

    expect(host.querySelector('[data-testid="decision-order-text"]')).toBeNull();
    expect(host.querySelector('[data-testid="decision-copy-order"]')).toBeNull();
    expect(host.textContent).toContain(HOLD_NO_ORDER_TEXT);
    expect(host.querySelector('[data-testid="decision-no-trade-wall"]')).not.toBeNull();
  });

  it('copies the order text and says so', () => {
    copy.mockReturnValue(true);
    const {host} = render('confirm');

    host.querySelector<HTMLElement>('[data-testid="decision-copy-order"] button')?.click();

    expect(copy).toHaveBeenCalledWith('SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR');
    expect(success).toHaveBeenCalledWith(ORDER_COPIED_MESSAGE);
  });

  it('stays quiet when the clipboard refuses', () => {
    copy.mockReturnValue(false);
    const {host} = render('confirm');

    host.querySelector<HTMLElement>('[data-testid="decision-copy-order"] button')?.click();

    expect(success).not.toHaveBeenCalled();
  });

  it('confirms with an optional note, then toasts and closes with the decided proposal', () => {
    const {submit, sendButton} = render('confirm');
    expect(sendButton()?.disabled).toBe(false);

    submit();
    expect(service.confirm).toHaveBeenCalledWith('rec-1', {note: null});
    expect(sendButton()?.disabled).toBe(true);

    const decided = mkRecommendation({status: 'Confirmed'});
    response.next(decided);
    expect(success).toHaveBeenCalledWith(
      'Decision recorded. Place the GRAB order at IBKR yourself.'
    );
    expect(close).toHaveBeenCalledWith(decided);

    TestBed.resetTestingModule();
    response = new Subject<RecommendationDto>();
    service.confirm.mockReturnValue(response);
    const second = render('confirm');
    second.type('  Trimming before earnings  ');
    second.submit();
    expect(service.confirm).toHaveBeenLastCalledWith('rec-1', {note: 'Trimming before earnings'});
  });

  it('needs a reason to reject and sends it trimmed', () => {
    const {type, submit, sendButton, host} = render('reject');
    expect(host.querySelector('[data-testid="decision-no-trade-wall"]')).toBeNull();
    expect(sendButton()?.disabled).toBe(true);

    submit();
    expect(service.reject).not.toHaveBeenCalled();

    type('   ');
    expect(sendButton()?.disabled).toBe(true);

    type('  Still a core holding  ');
    expect(sendButton()?.disabled).toBe(false);
    submit();
    expect(service.reject).toHaveBeenCalledWith('rec-1', {reason: 'Still a core holding'});

    response.next(mkRecommendation({status: 'Rejected'}));
    expect(success).toHaveBeenCalledWith('Rejection recorded for GRAB.');
    expect(close).toHaveBeenCalledOnce();
  });

  it('resolves a registered error and keeps the sheet open', () => {
    service.confirm.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 409, error: {errorCode: 'RECOMMENDATION_NOT_PENDING'}})
      )
    );
    const {host, submit, sendButton} = render('confirm');

    submit();

    expect(host.querySelector('[data-testid="decision-error"]')?.textContent).toContain(
      ERROR_MESSAGES_REGISTRY['RECOMMENDATION_NOT_PENDING']
    );
    expect(sendButton()?.disabled).toBe(false);
    expect(close).not.toHaveBeenCalled();
  });

  it('falls back to the default message for an unregistered error', () => {
    service.reject.mockReturnValue(throwError(() => new Error('boom')));
    const {host, type, submit} = render('reject');

    type('No');
    submit();

    expect(host.querySelector('[data-testid="decision-error"]')?.textContent).toContain(
      DECISION_FAILED_MESSAGE
    );
  });
});
