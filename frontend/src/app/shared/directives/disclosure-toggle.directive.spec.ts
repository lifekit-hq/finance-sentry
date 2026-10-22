import {Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {DisclosureRowComponent} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {DisclosureToggleDirective} from './disclosure-toggle.directive';

const onToggle = vi.fn();

@Component({
  selector: 'fns-host',
  imports: [DisclosureRowComponent, DisclosureToggleDirective],
  template: `<cmn-disclosure-row
    [open]="false"
    (fnsDisclosureToggle)="onToggle($event)"
    label="Outer"
  >
    <cmn-disclosure-row id="inner" label="Inner" />
  </cmn-disclosure-row>`,
})
class HostComponent {
  public onToggle = onToggle;
}

describe('DisclosureToggleDirective', () => {
  it('reports the state the row’s own toggle moves to, not a nested row’s', () => {
    onToggle.mockClear();
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const outer = root.querySelector('button[aria-controls]') as HTMLButtonElement;

    outer.click();
    fixture.detectChanges();
    (root.querySelector('#inner button[aria-controls]') as HTMLButtonElement).click();
    outer.click();

    expect(onToggle.mock.calls).toEqual([[true], [false]]);
  });
});
