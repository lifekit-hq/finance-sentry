import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {AppUpdateService} from '@lifekit-hq/core/pwa';

import {AppComponent} from './app.component';

describe('AppComponent', () => {
  const updateReady = signal(false);
  const reload = vi.fn().mockResolvedValue(undefined);

  function render(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([]), {provide: AppUpdateService, useValue: {updateReady, reload}}],
    });
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    updateReady.set(false);
    reload.mockClear();
  });

  it('renders the shared offline, update and install prompts in the shell', () => {
    const host = render();

    expect(host.querySelector('lk-offline-banner')).not.toBeNull();
    expect(host.querySelector('lk-update-prompt')).not.toBeNull();
    expect(host.querySelector('lk-install-hint')).not.toBeNull();
  });

  it('shows the update prompt once AppUpdateService reports a downloaded version', () => {
    updateReady.set(true);
    const host = render();

    const prompt = host.querySelector('lk-update-prompt') as HTMLElement & {ready: boolean};

    expect(prompt.ready).toBe(true);
  });

  it('activates the new version when the prompt asks to reload', () => {
    const host = render();

    host
      .querySelector('lk-update-prompt')
      ?.dispatchEvent(new CustomEvent('lk-update-prompt-reload'));

    expect(reload).toHaveBeenCalledOnce();
  });
});
