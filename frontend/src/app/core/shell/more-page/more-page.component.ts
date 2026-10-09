import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {
  BadgeComponent,
  CardComponent,
  IconComponent,
  ListItemRowComponent,
  PageContainerComponent,
} from '@lifekit-hq/ui';

import {AuthStore} from '../../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {WHATS_NEW_LABEL} from '../../whats-new/constants/whats-new.constants';
import {WhatsNewPanelService} from '../../whats-new/services/whats-new-panel.service';
import {WhatsNewStore} from '../../whats-new/store/whats-new.store';
import {NAV_ITEMS} from '../app-shell.constants';
import {NavUtils} from '../utils/nav.utils';

/** The phone More tab: the nav items past the four tabs, as the 1.x tab bar's More sheet listed them. */
@Component({
  selector: 'fns-more-page',
  imports: [
    PageContainerComponent,
    BadgeComponent,
    CardComponent,
    IconComponent,
    ListItemRowComponent,
    RouterLink,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './more-page.component.html',
})
export class MorePageComponent {
  private readonly authStore = inject(AuthStore);
  private readonly whatsNewPanel = inject(WhatsNewPanelService);

  public readonly email = this.authStore.email;
  public readonly hasUnread = inject(WhatsNewStore).hasUnread;
  public readonly whatsNewLabel = WHATS_NEW_LABEL;
  public readonly versionLabel = `Version ${APP_VERSION}`;

  public readonly items = computed(() =>
    NavUtils.moreItems(NAV_ITEMS).filter(item =>
      NavUtils.isPermitted(item.route, this.authStore.permissions())
    )
  );

  public openWhatsNew(): void {
    this.whatsNewPanel.open();
  }

  public logout(): void {
    this.authStore.logout();
  }
}
