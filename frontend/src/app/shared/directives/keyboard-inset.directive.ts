import {DOCUMENT} from '@angular/common';
import {DestroyRef, Directive, ElementRef, inject} from '@angular/core';

const KEYBOARD_INSET_PROPERTY = '--fns-keyboard-inset';
const CONTENT_INSET_PROPERTY = '--cmn-content-inset-bottom';
/** Below this the covered strip is browser chrome (URL bar), not an on-screen keyboard. */
const KEYBOARD_MIN_INSET_PX = 100;
const MAX_UNZOOMED_SCALE = 1.01;

/**
 * Publishes how much of the page the on-screen keyboard covers as `--fns-keyboard-inset` on the host,
 * so a page that pins its own composer can shrink above the keyboard. The phone shell is `fixed inset-0`
 * on the layout viewport, which the keyboard does not resize, so without this a bottom composer sits
 * under it. The shell's own bottom inset (the floating tab bar) is already clear of the page, so it is
 * subtracted: only the part of the keyboard that rises above it counts.
 */
@Directive({selector: '[fnsKeyboardInset]'})
export class KeyboardInsetDirective {
  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const view = inject(DOCUMENT).defaultView;
    const viewport = view?.visualViewport;
    if (!view || !viewport) {
      return;
    }
    const update = (): void => {
      const covered = view.innerHeight - viewport.height - viewport.offsetTop;
      const keyboardOpen = viewport.scale <= MAX_UNZOOMED_SCALE && covered >= KEYBOARD_MIN_INSET_PX;
      const shellInset = parseFloat(
        getComputedStyle(host).getPropertyValue(CONTENT_INSET_PROPERTY)
      );
      const inset = keyboardOpen ? Math.max(0, Math.round(covered - (shellInset || 0))) : 0;
      host.style.setProperty(KEYBOARD_INSET_PROPERTY, `${inset}px`);
    };
    viewport.addEventListener('resize', update);
    viewport.addEventListener('scroll', update);
    inject(DestroyRef).onDestroy(() => {
      viewport.removeEventListener('resize', update);
      viewport.removeEventListener('scroll', update);
      host.style.removeProperty(KEYBOARD_INSET_PROPERTY);
    });
    update();
  }
}
