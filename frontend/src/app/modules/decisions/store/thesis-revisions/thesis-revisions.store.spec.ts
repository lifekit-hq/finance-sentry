import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {ThesisRevisionFixtures} from '../../../../shared/models/thesis-revision/thesis-revision.fixtures';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {OFFLINE_ERROR_MESSAGE} from '../../../../shared/utils/async-view.utils';
import {THESIS_REVISIONS_LOAD_FAILED_MESSAGE} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisService} from '../../services/thesis.service';
import {ThesisRevisionService} from '../../services/thesis-revision.service';
import {ThesisRevisionSheetService} from '../../services/thesis-revision-sheet.service';
import {ThesisRevisionsStore} from './thesis-revisions.store';

const {revision, thesis} = ThesisRevisionFixtures;

describe('ThesisRevisionsStore', () => {
  const offline = signal(false);
  let service: {list: ReturnType<typeof vi.fn>};
  let theses: {list: ReturnType<typeof vi.fn>};
  let sheet: {open: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof ThesisRevisionsStore> {
    TestBed.configureTestingModule({
      providers: [
        ThesisRevisionsStore,
        {provide: ThesisRevisionService, useValue: service},
        {provide: ThesisService, useValue: theses},
        {provide: ThesisRevisionSheetService, useValue: sheet},
        {provide: ConnectivityService, useValue: {offline}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(ThesisRevisionsStore);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    offline.set(false);
    service = {list: vi.fn().mockReturnValue(of({pending: [], decided: []}))};
    theses = {list: vi.fn().mockReturnValue(of([]))};
    sheet = {open: vi.fn().mockReturnValue(of(undefined))};
  });

  it('is loading until the queue arrives', () => {
    service.list.mockReturnValue(NEVER);

    expect(inject().view().status).toBe('loading');
  });

  it('loads the queue and the theses its cards read as their before', () => {
    const pending = revision();
    const decided = revision({id: 'rev-2', status: 'Rejected'});
    service.list.mockReturnValue(of({pending: [pending], decided: [decided]}));
    theses.list.mockReturnValue(of([thesis()]));
    const store = inject();

    expect(store.pending()).toEqual([pending]);
    expect(store.decided()).toEqual([decided]);
    expect(store.hasData()).toBe(true);
    expect(store.thesesById().get('thesis-1')?.ticker).toBe('CBRS');
    expect(store.view().status).toBe('success');
  });

  it('is empty, not an error, when nothing was ever proposed', () => {
    const store = inject();

    expect(store.hasData()).toBe(false);
    expect(store.view().status).toBe('success');
  });

  it('keeps the cards without a before when the theses cannot be read', () => {
    service.list.mockReturnValue(of({pending: [revision()], decided: []}));
    theses.list.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));
    const store = inject();

    expect(store.pending()).toHaveLength(1);
    expect(store.thesesById().size).toBe(0);
    expect(store.view().status).toBe('success');
  });

  it('reports a failed queue read as an error, never as empty, and resolves a registered code', () => {
    service.list.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 403, error: {errorCode: 'THESIS_REVISION_OWNER_ONLY'}})
      )
    );
    const store = inject();

    expect(store.view().status).toBe('error');
    expect(store.view().errorMessage).toBe(ERROR_MESSAGES_REGISTRY['THESIS_REVISION_OWNER_ONLY']);
  });

  it('falls back to the default message for an unregistered failure', () => {
    service.list.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));

    expect(inject().view().errorMessage).toBe(THESIS_REVISIONS_LOAD_FAILED_MESSAGE);
  });

  it('says offline while nothing is on screen', () => {
    offline.set(true);
    service.list.mockReturnValue(throwError(() => new HttpErrorResponse({status: 0})));

    expect(inject().view().errorMessage).toBe(OFFLINE_ERROR_MESSAGE);
  });

  it('moves a decided change from the queue to the head of the decided list and refreshes the theses', () => {
    const pending = revision();
    service.list.mockReturnValue(of({pending: [pending], decided: []}));
    const confirmed = {...pending, status: 'Confirmed' as const};
    sheet.open.mockReturnValue(of({revision: confirmed, thesis: thesis(), gate: null, note: null}));
    const store = inject();
    expect(theses.list).toHaveBeenCalledTimes(1);

    store.decide({revision: pending, verdict: 'confirm'});

    expect(sheet.open).toHaveBeenCalledWith(pending, 'confirm');
    expect(store.pending()).toEqual([]);
    expect(store.decided()).toEqual([confirmed]);
    expect(theses.list).toHaveBeenCalledTimes(2);
  });

  it('does not reread the theses after a rejection, which writes none', () => {
    const pending = revision();
    service.list.mockReturnValue(of({pending: [pending], decided: []}));
    sheet.open.mockReturnValue(
      of({revision: {...pending, status: 'Rejected'}, thesis: null, gate: null, note: null})
    );
    const store = inject();

    store.decide({revision: pending, verdict: 'reject'});

    expect(store.decided()[0].status).toBe('Rejected');
    expect(theses.list).toHaveBeenCalledTimes(1);
  });

  it('keeps the queue when the sheet is dismissed', () => {
    const pending = revision();
    service.list.mockReturnValue(of({pending: [pending], decided: []}));
    const store = inject();

    store.decide({revision: pending, verdict: 'confirm'});

    expect(store.pending()).toEqual([pending]);
    expect(store.decided()).toEqual([]);
  });
});
