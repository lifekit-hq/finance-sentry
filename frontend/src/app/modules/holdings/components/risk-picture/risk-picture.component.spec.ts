import {TestBed} from '@angular/core/testing';
import {ASYNC_STATE_SHOW_DELAY_MS} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, onTestFinished, vi} from 'vitest';

import {RISK_TOLERANCE_NOT_SET} from '../../constants/risk-picture/risk-picture.constants';
import {type RiskPicture} from '../../models/risk-picture/risk-picture.model';
import {RiskPictureService} from '../../services/risk-picture.service';
import {RiskPictureComponent} from './risk-picture.component';

const PICTURE: RiskPicture = {
  from: '2026-01-31',
  to: '2026-09-30',
  monthsObserved: 8,
  betaMonths: 8,
  volatility: {value: 0.182, reason: null},
  maxDrawdown: {value: 0.214, reason: null},
  beta: {value: 1.31, reason: null},
  drawdownTolerance: null,
  top1Symbol: 'DRAM',
  top1Weight: 0.378,
  top3Symbols: ['DRAM', 'BTC', 'VWCE'],
  top3Weight: 0.71,
  cryptoShare: 0.19,
  positionFlag: 0.3,
  top1OverFlag: true,
};

const FIGURE_COUNT = 6;

/**
 * The proof rule: a risk figure that renders without its one-line explanation or without its basis fails here.
 * Returns the figures it checked so a caller can also assert there were some.
 */
function expectEveryFigureExplained(root: ParentNode): Element[] {
  const figures = [...root.querySelectorAll('[data-risk-figure]')];
  for (const figure of figures) {
    const name = figure.getAttribute('data-testid');
    expect(
      figure.querySelector('[data-risk-explanation]')?.textContent?.trim(),
      `${name} explanation`
    ).toBeTruthy();
    expect(
      figure.querySelector('[data-risk-basis]')?.textContent?.trim(),
      `${name} basis`
    ).toBeTruthy();
  }
  return figures;
}

describe('RiskPictureComponent', () => {
  const render = (picture: RiskPicture): HTMLElement => {
    TestBed.configureTestingModule({
      providers: [{provide: RiskPictureService, useValue: {getRiskPicture: () => of(picture)}}],
    });
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    const fixture = TestBed.createComponent(RiskPictureComponent);
    fixture.detectChanges();
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  describe('explanation and basis rule', () => {
    it('allowed case: every rendered figure carries its explanation and its basis', () => {
      const figures = expectEveryFigureExplained(render(PICTURE));

      expect(figures).toHaveLength(FIGURE_COUNT);
    });

    it('blocked case: a figure rendered without its basis is caught', () => {
      const root = document.createElement('div');
      root.innerHTML =
        '<div data-risk-figure data-testid="risk-figure-beta"><p data-risk-explanation>Moves with the market.</p></div>';

      expect(() => expectEveryFigureExplained(root)).toThrow(/risk-figure-beta basis/);
    });

    it('blocked case: a figure rendered without its explanation is caught', () => {
      const root = document.createElement('div');
      root.innerHTML =
        '<div data-risk-figure data-testid="risk-figure-beta"><p data-risk-basis>Monthly returns.</p></div>';

      expect(() => expectEveryFigureExplained(root)).toThrow(/risk-figure-beta explanation/);
    });

    it('a figure with no number still carries both, and says why there is none', () => {
      const el = render({
        ...PICTURE,
        volatility: {value: null, reason: 'NotEnoughHistory'},
        beta: {value: null, reason: 'NotEnoughHistory'},
      });

      expect(expectEveryFigureExplained(el)).toHaveLength(FIGURE_COUNT);
      expect(el.querySelector('[data-testid="risk-value-volatility"]')?.textContent).toContain(
        'not enough history'
      );
    });
  });

  it('shows the drawdown as "tolerance not set" when the tolerance is NULL', () => {
    const el = render(PICTURE);

    expect(el.querySelector('[data-testid="risk-value-max-drawdown"]')?.textContent).toContain(
      `21.4%, ${RISK_TOLERANCE_NOT_SET}`
    );
  });

  it('shows the drawdown against the tolerance when one is set', () => {
    const el = render({...PICTURE, drawdownTolerance: 0.25});

    expect(el.querySelector('[data-testid="risk-value-max-drawdown"]')?.textContent).toContain(
      '21.4% vs 25.0% tolerance'
    );
  });

  it('states the position flag it used beside the top-1 weight and marks a breach', () => {
    const el = render(PICTURE);

    const top1 = el.querySelector('[data-testid="risk-figure-largest-position"]');
    expect(top1?.textContent).toContain('37.8% (DRAM)');
    expect(top1?.textContent).toContain('Position flag used: 30.0%');
    expect(el.querySelector('[data-testid="risk-value-largest-position"]')?.className).toContain(
      'text-status-warning'
    );
  });

  it('says no flag is stored rather than inventing one', () => {
    const el = render({...PICTURE, positionFlag: null, top1OverFlag: null});

    expect(el.querySelector('[data-testid="risk-figure-largest-position"]')?.textContent).toContain(
      'Position flag: none stored.'
    );
  });

  it('shows top-3 weight, crypto share and beta', () => {
    const el = render(PICTURE);

    expect(el.querySelector('[data-testid="risk-value-top-three"]')?.textContent).toContain(
      '71.0%'
    );
    expect(el.querySelector('[data-testid="risk-value-crypto"]')?.textContent).toContain('19.0%');
    expect(el.querySelector('[data-testid="risk-value-beta"]')?.textContent).toContain('1.31');
  });
});
