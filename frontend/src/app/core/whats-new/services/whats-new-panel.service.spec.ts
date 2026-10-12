import {TestBed} from '@angular/core/testing';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {WhatsNewPanelComponent} from '../components/whats-new-panel/whats-new-panel.component';
import {WhatsNewStore} from '../store/whats-new.store';
import {WhatsNewPanelService} from './whats-new-panel.service';

describe('WhatsNewPanelService', () => {
  const open = vi.fn();
  const store = {load: vi.fn(), markSeen: vi.fn()};

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {provide: CmnDrawerService, useValue: {open}},
        {provide: WhatsNewStore, useValue: store},
      ],
    });
  });

  it('loads the content, clears the dot and opens the drawer', () => {
    TestBed.inject(WhatsNewPanelService).open();

    expect(store.load).toHaveBeenCalledOnce();
    expect(store.markSeen).toHaveBeenCalledOnce();
    expect(open).toHaveBeenCalledWith(
      WhatsNewPanelComponent,
      expect.objectContaining({title: "What's new"})
    );
  });
});
