import {type HttpEvent, HttpEventType} from '@angular/common/http';
import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {concatMap, defer, Observable} from 'rxjs';

import {type AgentSseEvent, type SendChatRequest} from '../models/chat/chat.model';
import {
  type ConversationDetail,
  type ConversationSummary,
} from '../models/conversation/conversation.model';

const SSE_DELIMITER = '\n\n';

@Injectable({providedIn: 'root'})
export class AgentService extends ApiService {
  constructor() {
    super('agent');
  }

  private static str(value: unknown, fallback = ''): string {
    return typeof value === 'string' ? value : fallback;
  }

  private static bodySoFar(event: HttpEvent<string>): string {
    switch (event.type) {
      case HttpEventType.DownloadProgress:
        return event.partialText ?? '';
      case HttpEventType.Response:
        return event.body ?? '';
      default:
        return '';
    }
  }

  private static parseFrame(frame: string): AgentSseEvent | null {
    let eventName = 'message';
    const dataLines: string[] = [];

    for (const line of frame.split('\n')) {
      if (line.startsWith('event:')) {
        eventName = line.slice('event:'.length).trim();
      } else if (line.startsWith('data:')) {
        dataLines.push(line.slice('data:'.length).trim());
      }
    }

    if (dataLines.length === 0) {
      return null;
    }

    const payload = JSON.parse(dataLines.join('\n')) as Record<string, unknown>;
    return AgentService.toEvent(eventName, payload);
  }

  private static toEvent(name: string, payload: Record<string, unknown>): AgentSseEvent | null {
    switch (name) {
      case 'conversation':
        return {type: 'conversation', conversationId: AgentService.str(payload['conversationId'])};
      case 'text':
        return {type: 'text', delta: AgentService.str(payload['delta'])};
      case 'tool':
        return {
          type: 'tool',
          name: AgentService.str(payload['name']),
          phase: payload['phase'] === 'end' ? 'end' : 'start',
        };
      case 'error':
        return {
          type: 'error',
          code: AgentService.str(payload['code'], 'llm_unavailable'),
          message: AgentService.str(payload['message']),
        };
      case 'done':
        return {type: 'done', messageId: AgentService.str(payload['messageId'])};
      default:
        return null;
    }
  }

  public listConversations(): Observable<ConversationSummary[]> {
    return this.get<ConversationSummary[]>('conversations');
  }

  public getConversation(id: string): Observable<ConversationDetail> {
    return this.get<ConversationDetail>(`conversations/${id}`);
  }

  public deleteConversation(id: string): Observable<void> {
    return this.delete<void>(`conversations/${id}`);
  }

  /**
   * Streams the chat reply over SSE through HttpClient, so the auth interceptor's silent refresh
   * covers it like every other call. `partialText` is the cumulative body so far; the `event:`/`data:`
   * frames not yet consumed are parsed into a typed event each. Unsubscribing cancels the request.
   */
  public streamChat(request: SendChatRequest): Observable<AgentSseEvent> {
    return defer(() => {
      let consumed = 0;

      return this.http
        .post(`${this.baseUrl}/chat`, request, {
          headers: {accept: 'text/event-stream'},
          observe: 'events',
          reportProgress: true,
          responseType: 'text',
        })
        .pipe(
          concatMap(httpEvent => {
            const text = AgentService.bodySoFar(httpEvent);
            const events: AgentSseEvent[] = [];
            let boundary = text.indexOf(SSE_DELIMITER, consumed);
            while (boundary >= 0) {
              const event = AgentService.parseFrame(text.slice(consumed, boundary));
              if (event !== null) {
                events.push(event);
              }

              consumed = boundary + SSE_DELIMITER.length;
              boundary = text.indexOf(SSE_DELIMITER, consumed);
            }

            return events;
          })
        );
    });
  }
}
