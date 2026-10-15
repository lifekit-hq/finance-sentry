import {Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it, vi} from 'vitest';

import {DisclosureToggleDirective} from './disclosure-toggle.directive';

const onToggle = vi.fn();

@Component({
  selector: 'fns-host',
  imports: [DisclosureToggleDirective],
  template: `<cmn-disclosure-row (fnsDisclosureToggle)="onToggle($event)"
    ><details>
      <summary>x</summary>
      <details id="inner"></details></details
  ></cmn-disclosure-row>`,
  schemas: [],
})
class HostComponent {
  public onToggle = onToggle;
}

describe('DisclosureToggleDirective', () => {
  it('reports the open state of the row’s own details, not a nested one', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const outer = root.querySelector('cmn-disclosure-row > details') as HTMLDetailsElement;

    outer.open = true;
    outer.dispatchEvent(new Event('toggle'));
    root.querySelector('#inner')?.dispatchEvent(new Event('toggle'));

    expect(onToggle).toHaveBeenCalledTimes(1);
    expect(onToggle).toHaveBeenCalledWith(true);
  });
});
