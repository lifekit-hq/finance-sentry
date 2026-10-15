import {DestroyRef, Directive, ElementRef, inject, output} from '@angular/core';

/**
 * `cmn-disclosure-row` wraps a native `<details>` and exposes no open/close output, so a remembered
 * disclosure cannot hear the user toggle it. The `toggle` event does not bubble, but it can be caught on
 * the way down; this reports it until the library exposes an output.
 */
// Attaches by element + attribute so templates need no extra opt-in; the prefix rule targets attribute-only selectors.

@Directive({selector: 'cmn-disclosure-row[fnsDisclosureToggle]'})
export class DisclosureToggleDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  public readonly fnsDisclosureToggle = output<boolean>();

  constructor() {
    const onToggle = (event: Event): void => {
      if (event.target instanceof HTMLDetailsElement && event.target.parentElement === this.host) {
        this.fnsDisclosureToggle.emit(event.target.open);
      }
    };
    this.host.addEventListener('toggle', onToggle, true);
    inject(DestroyRef).onDestroy(() => this.host.removeEventListener('toggle', onToggle, true));
  }
}
