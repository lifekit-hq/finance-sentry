import {Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {afterEach, describe, expect, it, vi} from 'vitest';

import {KeyboardInsetDirective} from './keyboard-inset.directive';

@Component({
  selector: 'fns-test-host',
  imports: [KeyboardInsetDirective],
  template: '<div fnsKeyboardInset></div>',
})
class TestHostComponent {}

const WINDOW_HEIGHT = 800;
const KEYBOARD_PX = 300;
const TAB_BAR_PX = 60;

class FakeViewport extends EventTarget {
  public height = WINDOW_HEIGHT;
  public offsetTop = 0;
  public scale = 1;
}

describe('KeyboardInsetDirective', () => {
  const original = Object.getOwnPropertyDescriptor(window, 'visualViewport');

  afterEach(() => {
    if (original) {
      Object.defineProperty(window, 'visualViewport', original);
    } else {
      Reflect.deleteProperty(window, 'visualViewport');
    }
  });

  const setup = (shellInset?: string) => {
    const viewport = new FakeViewport();
    Object.defineProperty(window, 'visualViewport', {value: viewport, configurable: true});
    vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(WINDOW_HEIGHT);
    const fixture = TestBed.createComponent(TestHostComponent);
    const host: HTMLElement = fixture.nativeElement.querySelector('div');
    if (shellInset) {
      host.style.setProperty('--cmn-content-inset-bottom', shellInset);
    }
    fixture.detectChanges();
    return {fixture, viewport, host};
  };
  const inset = (host: HTMLElement) => host.style.getPropertyValue('--fns-keyboard-inset');

  it('publishes no inset while the keyboard is closed', () => {
    const {host} = setup();
    expect(inset(host)).toBe('0px');
  });

  it('publishes the covered height once the keyboard opens', () => {
    const {host, viewport} = setup();
    viewport.height = WINDOW_HEIGHT - KEYBOARD_PX;
    viewport.dispatchEvent(new Event('resize'));
    expect(inset(host)).toBe(`${KEYBOARD_PX}px`);
  });

  it('counts only the part of the keyboard that rises above the shell bottom inset', () => {
    const {host, viewport} = setup(`${TAB_BAR_PX}px`);
    viewport.height = WINDOW_HEIGHT - KEYBOARD_PX;
    viewport.dispatchEvent(new Event('resize'));
    expect(inset(host)).toBe(`${KEYBOARD_PX - TAB_BAR_PX}px`);
  });

  it('ignores a pinch zoom, which also shrinks the visual viewport', () => {
    const {host, viewport} = setup();
    viewport.height = WINDOW_HEIGHT - KEYBOARD_PX;
    viewport.scale = 2;
    viewport.dispatchEvent(new Event('resize'));
    expect(inset(host)).toBe('0px');
  });

  it('stops listening and clears the property on destroy', () => {
    const {fixture, host, viewport} = setup();
    fixture.destroy();
    viewport.height = WINDOW_HEIGHT - KEYBOARD_PX;
    viewport.dispatchEvent(new Event('resize'));
    expect(inset(host)).toBe('');
  });
});
