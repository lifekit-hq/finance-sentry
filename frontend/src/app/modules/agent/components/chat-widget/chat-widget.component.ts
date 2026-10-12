import {BreakpointObserver} from '@angular/cdk/layout';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {NavigationEnd, Router} from '@angular/router';
import {
  ChatComponent,
  CMN_MEDIA_MD,
  type CmnChatStreamFn,
  IconComponent,
  type LucideIconName,
} from '@lifekit-hq/ui';
import {filter, map} from 'rxjs';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AgentChatStore} from '../../store/agent-chat.store';

@Component({
  selector: 'fns-chat-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AgentChatStore],
  imports: [ChatComponent, IconComponent],
  templateUrl: './chat-widget.component.html',
})
export class ChatWidgetComponent {
  private readonly router = inject(Router);
  private readonly routerUrl = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects)
    ),
    {initialValue: this.router.url}
  );

  private readonly breakpoints = inject(BreakpointObserver);
  // The shared layout swaps the sidebar for the bottom tab bar below md; the FAB is desktop-only.
  private readonly isDesktop = toSignal(
    this.breakpoints.observe(CMN_MEDIA_MD).pipe(map(state => state.matches)),
    {initialValue: this.breakpoints.isMatched(CMN_MEDIA_MD)}
  );

  private readonly injector = inject(Injector);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly toggleButton = viewChild<ElementRef<HTMLElement>>('toggleButton');

  public readonly store = inject(AgentChatStore);
  public readonly panelId = 'fns-chat-widget-panel';
  public readonly isOpen = signal(false);
  // The full-page Ledger already hosts a chat — the floating widget would be redundant there.
  public readonly isHidden = computed(
    () => !this.isDesktop() || this.routerUrl().startsWith(AppRoute.Ledger)
  );

  protected readonly openIcon: LucideIconName = 'Sparkles';
  protected readonly closeIcon: LucideIconName = 'X';
  protected readonly newChatIcon: LucideIconName = 'Plus';

  public readonly chatStream: CmnChatStreamFn = text => this.store.stream(text);

  public toggle(): void {
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  /** Closes the panel and hands focus back to the toggle that opened it. */
  public close(): void {
    if (!this.isOpen()) {
      return;
    }
    this.isOpen.set(false);
    afterNextRender(() => this.toggleButton()?.nativeElement.focus(), {injector: this.injector});
  }

  private open(): void {
    this.isOpen.set(true);
    afterNextRender(
      () => {
        const panel = this.panel()?.nativeElement;
        const target = panel?.querySelector<HTMLElement>('textarea, input') ?? panel;
        target?.focus();
      },
      {injector: this.injector}
    );
  }

  public onNewChat(): void {
    this.store.resetThread();
  }
}
