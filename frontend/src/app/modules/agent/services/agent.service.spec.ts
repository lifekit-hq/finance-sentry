import {
  HttpEventType,
  HttpStatusCode,
  provideHttpClient,
  withInterceptors,
  withXhr,
} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {of} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {authInterceptor} from '../../auth/interceptors/auth.interceptor';
import {AuthService} from '../../auth/services/auth.service';
import {AuthStore} from '../../auth/store/auth.store';
import {type AgentSseEvent} from '../models/chat/chat.model';
import {AgentService} from './agent.service';

const CHAT_URL = 'http://api.test/agent/chat';
const REQUEST = {conversationId: null, message: 'hi'};

const CONVERSATION_FRAME = 'event: conversation\ndata: {"conversationId":"c1"}\n\n';
const TEXT_FRAME_HEL = 'event: text\ndata: {"delta":"Hel"}\n\n';
const TEXT_FRAME_LO = 'event: text\ndata: {"delta":"lo"}\n\n';
const DONE_FRAME = 'event: done\ndata: {"messageId":"m1"}\n\n';

describe('AgentService.streamChat', () => {
  const refresh = vi.fn();
  const applyAuthResponse = vi.fn();
  const expireSession = vi.fn();
  let service: AgentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    refresh.mockReset().mockReturnValue(of({accessToken: 'fresh'}));
    applyAuthResponse.mockReset();
    expireSession.mockReset();
    // The pre-fix implementation bypassed HttpClient; a 401 from raw fetch is what the user saw.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, {status: HttpStatusCode.Unauthorized}))
    );

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
        {provide: AuthService, useValue: {refresh}},
        {provide: AuthStore, useValue: {applyAuthResponse, expireSession}},
      ],
    });
    service = TestBed.inject(AgentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.unstubAllGlobals();
  });

  it('refreshes silently when the access cookie has expired and keeps streaming', () => {
    const events: AgentSseEvent[] = [];
    const errors: unknown[] = [];
    service.streamChat(REQUEST).subscribe({next: e => events.push(e), error: e => errors.push(e)});

    httpMock
      .expectOne(CHAT_URL)
      .flush(null, {status: HttpStatusCode.Unauthorized, statusText: 'Unauthorized'});

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(applyAuthResponse).toHaveBeenCalledWith({accessToken: 'fresh'});

    const retried = httpMock.expectOne(CHAT_URL);
    retried.event({
      type: HttpEventType.DownloadProgress,
      loaded: 1,
      partialText: CONVERSATION_FRAME,
    });

    expect(errors).toEqual([]);
    expect(events).toEqual([{type: 'conversation', conversationId: 'c1'}]);
  });

  it('emits each frame once as the cumulative partial text grows, and completes', () => {
    const events: AgentSseEvent[] = [];
    let completed = false;
    service
      .streamChat(REQUEST)
      .subscribe({next: e => events.push(e), complete: () => (completed = true)});

    const req = httpMock.expectOne(CHAT_URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(REQUEST);
    expect(req.request.withCredentials).toBe(true);

    // A frame split across two chunks is held back until its delimiter arrives.
    const chunkOne = CONVERSATION_FRAME + TEXT_FRAME_HEL.slice(0, 12);
    const chunkTwo = CONVERSATION_FRAME + TEXT_FRAME_HEL + TEXT_FRAME_LO;
    const full = chunkTwo + DONE_FRAME;
    req.event({
      type: HttpEventType.DownloadProgress,
      loaded: chunkOne.length,
      partialText: chunkOne,
    });
    expect(events).toEqual([{type: 'conversation', conversationId: 'c1'}]);

    req.event({
      type: HttpEventType.DownloadProgress,
      loaded: chunkTwo.length,
      partialText: chunkTwo,
    });
    req.flush(full);

    expect(events).toEqual([
      {type: 'conversation', conversationId: 'c1'},
      {type: 'text', delta: 'Hel'},
      {type: 'text', delta: 'lo'},
      {type: 'done', messageId: 'm1'},
    ]);
    expect(completed).toBe(true);
  });

  it('surfaces a non-401 failure as an error', () => {
    const errors: unknown[] = [];
    service.streamChat(REQUEST).subscribe({error: e => errors.push(e)});

    httpMock
      .expectOne(CHAT_URL)
      .flush(null, {status: HttpStatusCode.InternalServerError, statusText: 'Server Error'});

    expect(errors).toHaveLength(1);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('cancels the request when the subscription is unsubscribed', () => {
    const subscription = service.streamChat(REQUEST).subscribe();
    const req = httpMock.expectOne(CHAT_URL);

    subscription.unsubscribe();

    expect(req.cancelled).toBe(true);
  });
});
