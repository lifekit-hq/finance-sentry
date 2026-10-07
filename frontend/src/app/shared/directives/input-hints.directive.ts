import {AfterViewInit, Directive, ElementRef, inject} from '@angular/core';

const HINT_ATTRIBUTES = ['inputmode', 'enterkeyhint'] as const;

/**
 * `cmn-input` forwards only `autocomplete` to its inner `<input>`, so `inputmode` / `enterkeyhint`
 * written on the host never reach the element the mobile keyboard looks at. This copies the static
 * attributes down until the library exposes them.
 */
// Attaches by element + attribute so templates need no extra opt-in; the prefix rule targets attribute-only selectors.
// eslint-disable-next-line @angular-eslint/directive-selector
@Directive({selector: 'cmn-input[inputmode], cmn-input[enterkeyhint]'})
export class InputHintsDirective implements AfterViewInit {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  public ngAfterViewInit(): void {
    const field = this.host.querySelector('input');
    if (!field) {
      return;
    }
    for (const name of HINT_ATTRIBUTES) {
      const value = this.host.getAttribute(name);
      if (value !== null) {
        field.setAttribute(name, value);
      }
    }
  }
}
