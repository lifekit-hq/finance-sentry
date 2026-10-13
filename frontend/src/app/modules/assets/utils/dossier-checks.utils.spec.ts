import {describe, expect, it} from 'vitest';

import {
  type DossierCheckAxisDto,
  type DossierCheckAxisKey,
  type DossierCheckRuleDto,
} from '../models/dossier/dossier.model';
import {type WheelView} from '../models/dossier/dossier-view.model';
import {DossierChecksUtils} from './dossier-checks.utils';

function rule(overrides: Partial<DossierCheckRuleDto> = {}): DossierCheckRuleDto {
  return {
    key: 'pe_vs_history',
    label: 'P/E below its own 5-year average',
    passed: true,
    value: 28.54,
    threshold: 25.3,
    unit: 'multiple',
    note: null,
    ...overrides,
  };
}

function axis(key: DossierCheckAxisKey, passed: number): DossierCheckAxisDto {
  return {
    key,
    label: key,
    passed,
    evaluated: 2,
    rules: [rule({key: `${key}_a`}), rule({key: `${key}_b`})],
  };
}

const FIVE_AXES = [
  axis('analysts', 2),
  axis('growth', 1),
  axis('value', 0),
  axis('momentum', 2),
  axis('fit', 1),
];

describe('DossierChecksUtils.axes', () => {
  it('is empty without checks', () => {
    expect(DossierChecksUtils.axes(null)).toEqual([]);
  });

  it('maps every axis with its rule count and rules', () => {
    const [first] = DossierChecksUtils.axes({axes: [axis('value', 1)]});

    expect(first).toEqual(
      expect.objectContaining({key: 'value', passed: 1, evaluated: 2, total: 2})
    );
    expect(first.rules.map(r => r.key)).toEqual(['value_a', 'value_b']);
  });
});

describe('DossierChecksUtils.rule', () => {
  it('spells the rule out as a state, its fact against its bar and the note', () => {
    expect(DossierChecksUtils.rule(rule({note: 'median of 4 peers'}))).toEqual({
      key: 'pe_vs_history',
      label: 'P/E below its own 5-year average',
      state: 'pass',
      detail: '28.5x vs 25.3x',
      note: 'median of 4 peers',
    });
  });
});

describe('DossierChecksUtils.state', () => {
  it('reads an unevaluated rule as unknown', () => {
    expect(DossierChecksUtils.state(null)).toBe('unknown');
  });

  it('reads a passed rule as pass', () => {
    expect(DossierChecksUtils.state(true)).toBe('pass');
  });

  it('reads a failed rule as fail', () => {
    expect(DossierChecksUtils.state(false)).toBe('fail');
  });
});

describe('DossierChecksUtils.detail', () => {
  it('is null when the rule read no number', () => {
    expect(DossierChecksUtils.detail(rule({value: null}))).toBeNull();
  });

  it('is null for a yes/no rule', () => {
    expect(DossierChecksUtils.detail(rule({unit: 'none', value: 1}))).toBeNull();
  });

  it('shows the fact alone when there is no bar', () => {
    expect(DossierChecksUtils.detail(rule({unit: 'percent', value: 12.2, threshold: null}))).toBe(
      '12.2%'
    );
  });
});

describe('DossierChecksUtils.formatValue', () => {
  it('formats a percent', () => {
    expect(DossierChecksUtils.formatValue(10, 'percent')).toBe('10%');
  });

  it('formats a multiple', () => {
    expect(DossierChecksUtils.formatValue(25.25, 'multiple')).toBe('25.3x');
  });

  it('formats a unitless number', () => {
    expect(DossierChecksUtils.formatValue(3, 'none')).toBe('3');
  });
});

describe('DossierChecksUtils.wheel', () => {
  it('is null without checks', () => {
    expect(DossierChecksUtils.wheel(null)).toBeNull();
  });

  it('is null below three axes (crypto keeps momentum and fit)', () => {
    expect(DossierChecksUtils.wheel({axes: [axis('momentum', 1), axis('fit', 2)]})).toBeNull();
  });

  it('puts the first spoke straight up and reaches out by the share of rules passed', () => {
    const wheel = DossierChecksUtils.wheel({axes: FIVE_AXES}) as WheelView;

    expect(wheel.size).toBe(260);
    expect(wheel.centre).toBe(130);
    expect(wheel.axes[0]).toEqual(
      expect.objectContaining({key: 'analysts', x: 130, y: 60, anchor: 'middle'})
    );
    expect(wheel.rings).toHaveLength(2);
    // Analysts passed both rules: the shape reaches the full radius on the first spoke.
    expect(wheel.shape.split(' ')[0]).toBe('130,60');
    // Value passed none: that vertex sits on the centre.
    expect(wheel.shape.split(' ')[2]).toBe('130,130');
  });

  it('anchors labels right of centre at the start and left of centre at the end', () => {
    const wheel = DossierChecksUtils.wheel({axes: FIVE_AXES}) as WheelView;

    expect(wheel.axes[1].anchor).toBe('start');
    expect(wheel.axes[4].anchor).toBe('end');
  });
});
