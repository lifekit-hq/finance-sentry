import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {type MenuItem, MorePageComponent as MorePageKitComponent} from '@lifekit-hq/ui';

import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {PHONE_TAB_ROUTES} from '../app-shell.constants';
import {ShellChromeService} from '../services/shell-chrome.service';

/**
 * The phone More tab: the nav items past the tabs, then the account (Settings, What's new, Report a
 * problem, Sign out) and the version. The layout is `cmn-more-page`; this binds it to the same nav and
 * account the shell shows and handles the items the app owns.
 */
@Component({
  selector: 'fns-more-page',
  imports: [MorePageKitComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <cmn-more-page
      [items]="navItems()"
      [tabRoutes]="tabRoutes"
      [account]="account()"
      [versionLabel]="versionLabel"
      (itemSelect)="select($event)"
    />
  `,
})
export class MorePageComponent {
  private readonly chrome = inject(ShellChromeService);

  public readonly navItems = this.chrome.navItems;
  public readonly account = this.chrome.account;
  public readonly tabRoutes = [...PHONE_TAB_ROUTES];
  public readonly versionLabel = `v${APP_VERSION}`;

  /** A route item (Settings) is a link the kit follows; Sign out and the extras are the app's. */
  public select(item: MenuItem): void {
    this.chrome.handle(item.id);
  }
}
