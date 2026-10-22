import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type BookVsSpy} from '../../models/book-performance/book-performance.model';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';
import {BookVsSpyComponent} from './book-vs-spy.component';

const COMPARISON: BookVsSpy = {
  from: '2025-10-19',
  bookReturn: '+12.0%',
  benchmarkReturn: '+10.0%',
  headline: null,
};

describe('BookVsSpyComponent', () => {
  let store: {
    status: ReturnType<typeof signal<AsyncStatus>>;
    errorMessage: ReturnType<typeof signal<string>>;
    comparison: ReturnType<typeof signal<Nullable<BookVsSpy>>>;
    load: ReturnType<typeof vi.fn>;
  };

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(BookVsSpyComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(async () => {
    store = {
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      comparison: signal<Nullable<BookVsSpy>>(COMPARISON),
      load: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [BookVsSpyComponent]})
      .overrideComponent(BookVsSpyComponent, {
        set: {providers: [{provide: BookPerformanceStore, useValue: store}]},
      })
      .compileComponents();
  });

  it('puts the book and SPY returns on one line from the common start date', () => {
    const el = render();

    expect(el.querySelector('[data-testid="book-vs-spy"]')?.textContent).toContain(
      'since 19 Oct 2025'
    );
    expect(el.querySelector('[data-testid="book-vs-spy-returns"]')?.textContent?.trim()).toBe(
      '+12.0% vs +10.0%'
    );
  });

  it('renders nothing when no window was measured for both', () => {
    store.comparison.set(null);

    expect(render().querySelector('[data-testid="book-vs-spy"]')).toBeNull();
  });

  it('shows a placeholder while loading', () => {
    store.status.set('loading');

    const el = render();

    expect(el.querySelector('cmn-skeleton')).not.toBeNull();
    expect(el.querySelector('[data-testid="book-vs-spy"]')).toBeNull();
  });

  it('says the request failed and retries it, instead of reading as no window measured', () => {
    store.status.set('error');
    store.errorMessage.set('Failed to load the book against SPY.');
    store.comparison.set(null);

    const el = render();

    expect(el.textContent).toContain('Failed to load the book against SPY.');
    expect(el.querySelector('[data-testid="book-vs-spy"]')).toBeNull();
    const retry = Array.from(el.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Retry')
    );
    retry?.click();
    expect(store.load).toHaveBeenCalledTimes(1);
  });
});
