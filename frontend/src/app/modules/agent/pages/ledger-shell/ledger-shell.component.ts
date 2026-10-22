import {ChangeDetectionStrategy, Component} from '@angular/core';
import {RouterOutlet} from '@angular/router';
import {type CmnTab, TabGroupComponent} from '@lifekit-hq/ui';

import {KeyboardInsetDirective} from '../../../../shared/directives/keyboard-inset.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';

/**
 * The Ledger tab: the chat, and the Decisions queue of Ledger's proposals awaiting the owner's verdict.
 * On phone the page frame already fits the slot under the title, minus whatever the on-screen keyboard
 * covers; from md the 8rem is the top bar plus the large title, so the page never adds an outer scroll.
 */
@Component({
  selector: 'fns-ledger-shell',
  imports: [RouterOutlet, TabGroupComponent],
  hostDirectives: [KeyboardInsetDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class:
      'flex h-[calc(100%-var(--fns-keyboard-inset,0px))] flex-col md:h-[calc(100dvh-8rem)] md:min-h-[24rem]',
  },
  template: `
    <cmn-tab-group [tabs]="tabs" ariaLabel="Ledger view" class="shrink-0 px-cmn-4 md:px-cmn-8" />
    <router-outlet />
  `,
})
export class LedgerShellComponent {
  public readonly tabs: CmnTab[] = [
    {id: 'chat', label: 'Chat', link: AppRoute.Ledger, exact: true},
    {id: 'decisions', label: 'Decisions', link: AppRoute.LedgerDecisions},
  ];
}
