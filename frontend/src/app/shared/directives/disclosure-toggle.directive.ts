import {DestroyRef, Directive, ElementRef, inject, output} from '@angular/core';

/**
 * `cmn-disclosure-row` exposes no open/close output, so a remembered disclosure cannot hear the user
 * toggle it. The row is one toggle button (`aria-expanded`, `aria-controls` naming its body); this
 * catches a click on that button on the way down, before the row flips, and reports the state the
 * row is about to take, until the library exposes an output.
 */
// Attaches by element + attribute so templates need no extra opt-in; the prefix rule targets attribute-only selectors.

@Directive({selector: 'cmn-disclosure-row[fnsDisclosureToggle]'})
export class DisclosureToggleDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  public readonly fnsDisclosureToggle = output<boolean>();

  constructor() {
    const onClick = (event: Event): void => {
      // The row's own toggle comes first; a nested row's toggle or an action's control does not count.
      const toggle = this.host.querySelector('button[aria-controls]');
      if (toggle && event.target instanceof Element && event.target.closest('button') === toggle) {
        this.fnsDisclosureToggle.emit(toggle.getAttribute('aria-expanded') !== 'true');
      }
    };
    this.host.addEventListener('click', onClick, true);
    inject(DestroyRef).onDestroy(() => this.host.removeEventListener('click', onClick, true));
  }
}
