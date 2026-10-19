import {ChangeDetectionStrategy, Component, input} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {ScrollIntoViewDirective} from './scroll-into-view.directive';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'fns-host',
  imports: [ScrollIntoViewDirective],
  template: '<div [fnsScrollIntoView]="target()"></div>',
})
class HostComponent {
  public readonly target = input(false);
}

describe('ScrollIntoViewDirective', () => {
  it('scrolls the host into view on first render when asked to', () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentRef.setInput('target', true);
    fixture.detectChanges();

    expect(scroll).toHaveBeenCalledTimes(1);
  });

  it('leaves the page where it is otherwise', () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    TestBed.createComponent(HostComponent).detectChanges();

    expect(scroll).not.toHaveBeenCalled();
  });
});
