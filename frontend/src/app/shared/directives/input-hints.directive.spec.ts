import {ChangeDetectionStrategy, Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {InputHintsDirective} from './input-hints.directive';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'fns-host',
  imports: [InputHintsDirective],
  template: `
    <cmn-input inputmode="decimal" enterkeyhint="done">
      <!-- eslint-disable-next-line lifekit/no-raw-controls -- stands in for cmn-input's inner input -->
      <input />
    </cmn-input>
  `,
})
class HostComponent {}

describe('InputHintsDirective', () => {
  it('copies inputmode and enterkeyhint onto the inner input', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const field = (fixture.nativeElement as HTMLElement).querySelector('input');
    expect(field?.getAttribute('inputmode')).toBe('decimal');
    expect(field?.getAttribute('enterkeyhint')).toBe('done');
  });
});
