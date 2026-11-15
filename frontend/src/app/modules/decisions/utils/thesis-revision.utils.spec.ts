import {describe, expect, it} from 'vitest';

import {ThesisRevisionFixtures} from '../../../shared/models/thesis-revision/thesis-revision.fixtures';
import {ThesisRevisionUtils} from './thesis-revision.utils';

const {revision, content, thesis, trigger} = ThesisRevisionFixtures;

describe('ThesisRevisionUtils.changes', () => {
  it('shows the text against the thesis as it stands', () => {
    const [change] = ThesisRevisionUtils.changes(revision(), thesis());

    expect(change.field).toBe('text');
    expect(change.shape).toBe('text');
    expect(change.before).toBe('Wafer-scale inference wins on cost per token.');
    expect(change.after).toContain('two hyperscaler anchors');
  });

  it('leaves a field the proposal does not touch out', () => {
    const changes = ThesisRevisionUtils.changes(
      revision({kind: 'TriggerChange', proposed: content({invalidationTriggers: []})}),
      thesis()
    );

    expect(changes.map(c => c.field)).toEqual(['triggers']);
  });

  it('diffs triggers by their sentence: what is added, removed and kept', () => {
    const kept = trigger();
    const dropped = trigger({metric: 'net_margin', threshold: 0.1});
    const added = trigger({metric: 'price_drawdown', direction: 'greaterThan', threshold: 0.4});

    const [change] = ThesisRevisionUtils.changes(
      revision({kind: 'TriggerChange', proposed: content({invalidationTriggers: [kept, added]})}),
      thesis({invalidationTriggers: [kept, dropped]})
    );

    expect(change.shape).toBe('list');
    expect(change.added).toEqual(['Price drawdown rises above 40.0%']);
    expect(change.removed).toEqual(['Net margin falls below 10.0%']);
    expect(change.kept).toEqual(['Gross margin falls below 35.0%']);
  });

  it('clears a list when the proposal sends an empty one', () => {
    const [change] = ThesisRevisionUtils.changes(
      revision({proposed: content({narrativeFalsifiers: []})}),
      thesis({narrativeFalsifiers: ['Hyperscalers build in-house']})
    );

    expect(change.removed).toEqual(['Hyperscalers build in-house']);
    expect(change.added).toEqual([]);
  });

  it('formats data points and catalysts as one line each', () => {
    const changes = ThesisRevisionUtils.changes(
      revision({
        proposed: content({
          keyDataPoints: [
            {metric: 'gross_margin', direction: 'lessThan', threshold: 0.4, source: '10-K'},
          ],
          catalysts: [{date: '2027-02-01', event: 'Q4 print'}],
        }),
      }),
      thesis()
    );

    expect(changes[0].added).toEqual(['gross_margin < 0.4 (10-K)']);
    expect(changes[1].added).toEqual(['2027-02-01 · Q4 print']);
  });

  it('keeps the data points and catalysts the thesis already has, and drops a missing source', () => {
    const changes = ThesisRevisionUtils.changes(
      revision({
        proposed: content({
          keyDataPoints: [{metric: 'revenue', direction: 'greaterThan', threshold: 5, source: ''}],
          catalysts: [{date: '2027-02-01', event: 'Q4 print'}],
        }),
      }),
      thesis({
        keyDataPoints: [{metric: 'revenue', direction: 'greaterThan', threshold: 5, source: ''}],
        catalysts: [{date: '2027-02-01', event: 'Q4 print'}],
      })
    );

    expect(changes[0].kept).toEqual(['revenue > 5']);
    expect(changes[0].added).toEqual([]);
    expect(changes[1].kept).toEqual(['2027-02-01 · Q4 print']);
  });

  it('shows the entry price before as money when the thesis had one', () => {
    const [change] = ThesisRevisionUtils.changes(
      revision({proposed: content({entryPrice: 50})}),
      thesis({entryPrice: 42.5})
    );

    expect(change.before).toBe('$42.50');
    expect(change.after).toBe('$50.00');
  });

  it('shows the entry price as money, and none before when it was unset', () => {
    const [change] = ThesisRevisionUtils.changes(
      revision({proposed: content({entryPrice: 42.5})}),
      thesis({entryPrice: null})
    );

    expect(change.before).toBeNull();
    expect(change.after).toBe('$42.50');
  });

  it('reads a promotion against no thesis: everything proposed is new', () => {
    const [change] = ThesisRevisionUtils.changes(
      revision({kind: 'Promote', proposed: content({invalidationTriggers: [trigger()]})}),
      null
    );

    expect(change.added).toEqual(['Gross margin falls below 35.0%']);
    expect(change.removed).toEqual([]);
  });

  it('lists nothing for a close, which proposes no content', () => {
    expect(
      ThesisRevisionUtils.changes(revision({kind: 'Close', proposed: content()}), thesis())
    ).toEqual([]);
  });
});

describe('ThesisRevisionUtils.usesDefaults', () => {
  it('is true for a promotion with neither text nor triggers', () => {
    expect(ThesisRevisionUtils.usesDefaults(revision({kind: 'Promote', proposed: content()}))).toBe(
      true
    );
  });

  it('is false once the promotion carries its own text or triggers', () => {
    expect(
      ThesisRevisionUtils.usesDefaults(
        revision({kind: 'Promote', proposed: content({thesisText: 'x'})})
      )
    ).toBe(false);
  });

  it('is false for any other kind', () => {
    expect(ThesisRevisionUtils.usesDefaults(revision({kind: 'Close', proposed: content()}))).toBe(
      false
    );
  });
});
