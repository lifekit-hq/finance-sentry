import {ChangeDetectionStrategy, Component} from '@angular/core';
import {RouterOutlet} from '@angular/router';
import {type CmnTab, TabGroupComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';

/** The Ledger tab: the chat, and the Decisions queue of Ledger's proposals awaiting the owner's verdict. */
@Component({
  selector: 'fns-ledger-shell',
  imports: [RouterOutlet, TabGroupComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'flex min-h-full flex-col md:h-full'},
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
