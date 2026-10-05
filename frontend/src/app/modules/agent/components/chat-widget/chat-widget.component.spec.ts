import {BreakpointObserver, type BreakpointState} from '@angular/cdk/layout';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {of} from 'rxjs';

import {AgentChatStore} from '../../store/agent-chat.store';
import {ChatWidgetComponent} from './chat-widget.component';

const OPEN_BUTTON = 'button[aria-label="Open Ledger chat"]';

function render(isDesktop: boolean): HTMLElement {
  const state: BreakpointState = {matches: isDesktop, breakpoints: {}};
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: BreakpointObserver,
        useValue: {observe: () => of(state), isMatched: () => isDesktop},
      },
    ],
  });
  TestBed.overrideComponent(ChatWidgetComponent, {
    set: {providers: [{provide: AgentChatStore, useValue: {}}]},
  });
  const fixture = TestBed.createComponent(ChatWidgetComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ChatWidgetComponent floating button', () => {
  it('is absent below the md breakpoint (phone)', () => {
    expect(render(false).querySelector(OPEN_BUTTON)).toBeNull();
  });

  it('is present at the md breakpoint and above (desktop)', () => {
    expect(render(true).querySelector(OPEN_BUTTON)).not.toBeNull();
  });
});
