import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {AsyncStateComponent, ButtonComponent} from '@lifekit-hq/ui';

import {WhatsNewStore} from '../../store/whats-new.store';
import {WhatsNewVersionComponent} from '../whats-new-version/whats-new-version.component';

/** The body of the What's new drawer: the newest release first, older ones after. */
@Component({
  selector: 'fns-whats-new-panel',
  imports: [AsyncStateComponent, ButtonComponent, WhatsNewVersionComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <cmn-async-state
      [status]="store.viewStatus()"
      [errorMessage]="store.errorMessage()"
      [isEmpty]="store.visibleVersions().length === 0"
      [skeletonRows]="3"
      class="block p-cmn-4 md:p-cmn-6"
      emptyMessage="Nothing to show yet"
      skeletonHeight="3rem"
    >
      <div class="space-y-cmn-6" data-testid="whats-new-list">
        @for (version of store.visibleVersions(); track version.version) {
          <fns-whats-new-version [version]="version" />
        }
      </div>
      <cmn-button
        (clicked)="store.load()"
        error-action
        data-testid="whats-new-retry"
        size="sm"
        variant="secondary"
      >
        Retry
      </cmn-button>
    </cmn-async-state>
  `,
})
export class WhatsNewPanelComponent {
  public readonly store = inject(WhatsNewStore);
}
