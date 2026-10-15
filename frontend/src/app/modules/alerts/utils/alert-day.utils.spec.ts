import {type Alert} from '../models/alert/alert.model';
import {AlertDayUtils} from './alert-day.utils';

const NOW = new Date(2026, 9, 15, 12, 0, 0);

function makeAlert(id: string, createdAt: string): Alert {
  return {
    id,
    type: 'LowBalance',
    severity: 'Info',
    title: id,
    message: '',
    referenceId: null,
    referenceLabel: null,
    isRead: false,
    isResolved: false,
    createdAt,
    resolvedAt: null,
    occurrenceCount: 1,
    lastOccurredAt: createdAt,
  };
}

function localIso(year: number, month: number, day: number, hour: number): string {
  return new Date(year, month, day, hour).toISOString();
}

describe('AlertDayUtils', () => {
  describe('groupByDay', () => {
    it('returns no groups for an empty page', () => {
      expect(AlertDayUtils.groupByDay([], NOW)).toEqual([]);
    });

    it('groups consecutive alerts of a day and keeps the incoming order', () => {
      const a = makeAlert('a', localIso(2026, 9, 15, 9));
      const b = makeAlert('b', localIso(2026, 9, 15, 1));
      const c = makeAlert('c', localIso(2026, 9, 14, 23));
      const d = makeAlert('d', localIso(2026, 9, 10, 8));

      const groups = AlertDayUtils.groupByDay([a, b, c, d], NOW);

      expect(groups.map(g => [g.dayKey, g.label, g.items.map(i => i.id)])).toEqual([
        ['2026-10-15', 'Today', ['a', 'b']],
        ['2026-10-14', 'Yesterday', ['c']],
        ['2026-10-10', 'Sat 10 Oct', ['d']],
      ]);
    });

    it('adds the year to a day outside the current one', () => {
      const [group] = AlertDayUtils.groupByDay([makeAlert('a', localIso(2025, 11, 31, 10))], NOW);

      expect(group.label).toBe('Wed 31 Dec 2025');
    });

    it('defaults now to the current time', () => {
      const [group] = AlertDayUtils.groupByDay([makeAlert('a', new Date().toISOString())]);

      expect(group.label).toBe('Today');
    });
  });

  describe('dayKey', () => {
    it('reads the instant in the viewer time zone', () => {
      expect(AlertDayUtils.dayKey(localIso(2026, 9, 15, 0))).toBe('2026-10-15');
    });

    it('keeps unparseable text as its own key', () => {
      expect(AlertDayUtils.dayKey('not a date')).toBe('not a date');
    });
  });
});
