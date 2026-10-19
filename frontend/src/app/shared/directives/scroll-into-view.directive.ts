import {afterNextRender, Directive, ElementRef, inject, input} from '@angular/core';

/**
 * Brings the host into view once it first renders, when `fnsScrollIntoView` is true: a deep link
 * (`?provider=`) lands on its target instead of leaving it somewhere down the page.
 */
@Directive({selector: '[fnsScrollIntoView]'})
export class ScrollIntoViewDirective {
  public readonly fnsScrollIntoView = input(false);

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    afterNextRender(() => {
      if (this.fnsScrollIntoView()) {
        host.scrollIntoView({block: 'start'});
      }
    });
  }
}
