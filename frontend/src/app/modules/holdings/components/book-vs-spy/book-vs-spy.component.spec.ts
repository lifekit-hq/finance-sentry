import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {ASYNC_STATE_SHOW_DELAY_MS, type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, onTestFinished, vi} from 'vitest';

import {BOOK_VS_SPY_BASIS_NOTE} from '../../constants/book-performance/book-performance.constants';
import {
  type BookVsSpy,
  type BookVsSpyShort,
  type SustainedUnderperformance,
} from '../../models/book-performance/book-performance.model';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';
import {BookVsSpyComponent} from './book-vs-spy.component';

const COMPARISON: BookVsSpy = {
  from: '2025-10-19',
  bookReturn: '+12.0%',
  benchmarkReturn: '+10.0%',
  qqqReturn: '+14.0%',
  headline: null,
};

describe('BookVsSpyComponent', () => {
  let store: {
    status: ReturnType<typeof signal<AsyncStatus>>;
    errorMessage: ReturnType<typeof signal<string>>;
    comparison: ReturnType<typeof signal<Nullable<BookVsSpy>>>;
    shortComparison: ReturnType<typeof signal<Nullable<BookVsSpyShort>>>;
    sustained: ReturnType<typeof signal<Nullable<SustainedUnderperformance>>>;
    load: ReturnType<typeof vi.fn>;
  };

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(BookVsSpyComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const renderAfterShowDelay = (): HTMLElement => {
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    const fixture = TestBed.createComponent(BookVsSpyComponent);
    fixture.detectChanges();
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(async () => {
    store = {
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      comparison: signal<Nullable<BookVsSpy>>(COMPARISON),
      shortComparison: signal<Nullable<BookVsSpyShort>>({
        bookReturn: '-16.4%',
        benchmarkReturn: '-2.3%',
      }),
      sustained: signal<Nullable<SustainedUnderperformance>>(null),
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

  it('adds QQQ over the same dates when it was measured', () => {
    expect(render().querySelector('[data-testid="book-vs-qqq-returns"]')?.textContent?.trim()).toBe(
      'QQQ +14.0%'
    );
  });

  it('leaves QQQ out when it was not measured over the book window', () => {
    store.comparison.set({...COMPARISON, qqqReturn: null});

    expect(render().querySelector('[data-testid="book-vs-qqq-returns"]')).toBeNull();
  });

  it('renders nothing when no window was measured for both', () => {
    store.comparison.set(null);

    expect(render().querySelector('[data-testid="book-vs-spy"]')).toBeNull();
  });

  it('shows a placeholder while loading', () => {
    store.status.set('loading');

    const el = renderAfterShowDelay();

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

  describe('Home figures and their basis', () => {
    const text = (el: HTMLElement, id: string) =>
      el.querySelector(`[data-testid="${id}"]`)?.textContent?.trim();

    // Blocked case: a return figure without its basis note must fail this test. Allowed case: with it, it passes.
    it('names the basis under the returns, so no figure renders without it', () => {
      const el = render();

      expect(el.querySelector('[data-testid="book-vs-spy-returns"]')).not.toBeNull();
      expect(text(el, 'book-vs-spy-basis')).toBe(BOOK_VS_SPY_BASIS_NOTE);
      expect(text(el, 'book-vs-spy-basis')).toContain('cash-inclusive time-weighted return');
      expect(text(el, 'book-vs-spy-basis')).toContain('SPY');
    });

    it('draws the basis note after every return figure', () => {
      const el = render();
      const basis = el.querySelector('[data-testid="book-vs-spy-basis"]');
      const lastFigure = el.querySelector('[data-testid="book-vs-spy-3m-returns"]');

      expect(
        lastFigure &&
          basis &&
          (lastFigure.compareDocumentPosition(basis) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0
      ).toBe(true);
    });

    it('shows the since-start and 3 month returns together', () => {
      const el = render();

      expect(text(el, 'book-vs-spy-returns')).toBe('+12.0% vs +10.0%');
      expect(text(el, 'book-vs-spy-3m-returns')).toBe('-16.4% vs -2.3%');
    });

    it('leaves the 3 month row out when it was not measured, keeping the basis note', () => {
      store.shortComparison.set(null);
      const el = render();

      expect(el.querySelector('[data-testid="book-vs-spy-3m"]')).toBeNull();
      expect(text(el, 'book-vs-spy-basis')).toBe(BOOK_VS_SPY_BASIS_NOTE);
    });

    it('flags sustained underperformance only when the stored record says so', () => {
      expect(render().querySelector('[data-testid="book-vs-spy-sustained"]')).toBeNull();

      store.sustained.set({label: 'Equities'});
      expect(text(render(), 'book-vs-spy-sustained')).toContain('Equities');
    });

    it('renders no figure and no basis note when no window was measured', () => {
      store.comparison.set(null);
      const el = render();

      expect(el.querySelector('[data-testid="book-vs-spy-basis"]')).toBeNull();
    });
  });
});
