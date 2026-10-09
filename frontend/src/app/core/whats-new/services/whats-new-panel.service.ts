import {inject, Injectable} from '@angular/core';
import {CmnDrawerService} from '@lifekit-hq/ui';

import {WhatsNewPanelComponent} from '../components/whats-new-panel/whats-new-panel.component';
import {WHATS_NEW_DRAWER_WIDTH, WHATS_NEW_LABEL} from '../constants/whats-new.constants';
import {WhatsNewStore} from '../store/whats-new.store';

/** The one way into the panel (avatar menu, palette, More page): a side panel from 768px, a bottom sheet below. */
@Injectable({providedIn: 'root'})
export class WhatsNewPanelService {
  private readonly drawer = inject(CmnDrawerService);
  private readonly store = inject(WhatsNewStore);

  public open(): void {
    this.store.load();
    this.store.markSeen();
    this.drawer.open(WhatsNewPanelComponent, {
      title: WHATS_NEW_LABEL,
      width: WHATS_NEW_DRAWER_WIDTH,
    });
  }
}
