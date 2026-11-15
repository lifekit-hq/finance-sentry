import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {ASYNC_STATE_SHOW_DELAY_MS} from '@lifekit-hq/ui';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, onTestFinished, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {ThesisRevisionFixtures} from '../../../../shared/models/thesis-revision/thesis-revision.fixtures';
import {type ThesisRevisionsResult} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {THESIS_REVISIONS_EMPTY_COPY} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisService} from '../../services/thesis.service';
import {ThesisRevisionService} from '../../services/thesis-revision.service';
import {ThesisRevisionSheetService} from '../../services/thesis-revision-sheet.service';
import {ThesisRevisionsSectionComponent} from './thesis-revisions-section.component';

const {revision, thesis} = ThesisRevisionFixtures;

describe('ThesisRevisionsSectionComponent', () => {
  let list: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;

  const create = () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: ThesisRevisionService, useValue: {list}},
        {provide: ThesisRevisionSheetService, useValue: {open}},
        {provide: ThesisService, useValue: {list: () => of([thesis()])}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    const fixture = TestBed.createComponent(ThesisRevisionsSectionComponent);
    fixture.detectChanges();
    return {fixture, host: fixture.nativeElement as HTMLElement};
  };

  const render = (result: ThesisRevisionsResult) => {
    list.mockReturnValue(of(result));
    return create();
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    list = vi.fn().mockReturnValue(NEVER);
    open = vi.fn().mockReturnValue(of(undefined));
  });

  it('holds the place of the cards while the queue loads', () => {
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    const {fixture, host} = create();
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
    fixture.detectChanges();

    const skeleton = host.querySelector('[data-testid="thesis-revisions-skeleton"]');
    expect(skeleton?.querySelector('cmn-skeleton[shape="row"]')).not.toBeNull();
    expect(skeleton?.textContent).not.toContain(THESIS_REVISIONS_EMPTY_COPY.message);
  });

  it('shows the empty state with nothing proposed', () => {
    const {host} = render({pending: [], decided: []});

    expect(host.querySelector('[data-testid="thesis-revisions-empty"]')?.textContent).toContain(
      THESIS_REVISIONS_EMPTY_COPY.message
    );
    expect(host.querySelector('cmn-empty-state')).toBeNull();
    expect(host.querySelector('fns-thesis-revision-card')).toBeNull();
    expect(host.textContent).toContain('Thesis changes (0)');
  });

  it('shows the error with a retry that reads the queue again', () => {
    list.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 403, error: {errorCode: 'THESIS_REVISION_OWNER_ONLY'}})
      )
    );
    const {fixture, host} = create();

    expect(host.textContent).toContain(ERROR_MESSAGES_REGISTRY['THESIS_REVISION_OWNER_ONLY']);
    expect(host.textContent).not.toContain(THESIS_REVISIONS_EMPTY_COPY.message);

    list.mockReturnValue(of({pending: [revision()], decided: []}));
    host.querySelector<HTMLElement>('cmn-async-state button')?.click();
    fixture.detectChanges();

    expect(list).toHaveBeenCalledTimes(2);
    expect(host.querySelector('fns-thesis-revision-card')).not.toBeNull();
  });

  it('lists one card per pending change, oldest first, each against its thesis', () => {
    const {host} = render({
      pending: [
        revision({id: 'a', ticker: 'CBRS'}),
        revision({id: 'b', ticker: 'NVDA', subjectId: 'no-such-thesis'}),
      ],
      decided: [],
    });

    const cards = Array.from(host.querySelectorAll('fns-thesis-revision-card'));
    expect(
      cards.map(c => c.querySelector('header span.font-semibold, header a')?.textContent?.trim())
    ).toEqual(['CBRS', 'NVDA']);
    expect(cards[0].querySelector('[data-testid="thesis-revision-before"]')?.textContent).toContain(
      'Wafer-scale inference'
    );
    expect(cards[1].querySelector('[data-testid="thesis-revision-before"]')?.textContent).toContain(
      'No thesis yet'
    );
    expect(host.textContent).toContain('Thesis changes (2)');
  });

  it('opens the confirm sheet for the pressed card', () => {
    const pending = revision();
    const {host} = render({pending: [pending], decided: []});

    host.querySelector<HTMLElement>('[data-testid="thesis-revision-confirm"] button')?.click();

    expect(open).toHaveBeenCalledWith(pending, 'confirm');
  });

  it('keeps the decided list when nothing is pending, with the reason it was turned down', () => {
    const {host} = render({
      pending: [],
      decided: [
        revision({
          id: 'd',
          status: 'Rejected',
          decidedAt: '2026-11-12T10:00:00Z',
          decisionNote: 'The S-1 changes nothing.',
        }),
      ],
    });

    expect(host.querySelector('[data-testid="thesis-revisions-pending-empty"]')).not.toBeNull();
    const row = host.querySelector('[data-testid="thesis-revisions-decided"] li');
    expect(row?.textContent).toContain('Rejected');
    expect(row?.textContent).toContain('Update thesis');
    expect(row?.textContent).toContain('Nov 12, 2026');
    expect(row?.textContent).toContain('The S-1 changes nothing.');
  });
});
