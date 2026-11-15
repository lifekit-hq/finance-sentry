import {type Nullable} from '@lifekit-hq/ui';

import {
  type ThesisCatalyst,
  type ThesisDataPoint,
  type ThesisDto,
} from '../../../shared/models/thesis/thesis.model';
import {
  type ThesisRevisionChange,
  type ThesisRevisionChangeField,
  type ThesisRevisionDto,
} from '../../../shared/models/thesis-revision/thesis-revision.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {ThesisTriggerUtils} from '../../../shared/utils/thesis-trigger.utils';
import {THESIS_REVISION_CHANGE_LABEL} from '../constants/thesis-revision/thesis-revision.constants';

const LESS_THAN = 'lessThan';

export class ThesisRevisionUtils {
  /**
   * What confirming would change, field by field, against the thesis as it stands (null on a promotion). A field
   * the proposal leaves null is not listed; a list shows only what is added and removed, with the rest kept.
   */
  public static changes(
    revision: ThesisRevisionDto,
    thesis: Nullable<ThesisDto>
  ): ThesisRevisionChange[] {
    const {proposed} = revision;
    const changes: ThesisRevisionChange[] = [];

    if (proposed.thesisText !== null) {
      changes.push(
        ThesisRevisionUtils.textChange('text', thesis?.thesisText ?? null, proposed.thesisText)
      );
    }
    if (proposed.invalidationTriggers !== null) {
      changes.push(
        ThesisRevisionUtils.listChange(
          'triggers',
          (thesis?.invalidationTriggers ?? []).map(t => ThesisTriggerUtils.describe(t)),
          proposed.invalidationTriggers.map(t => ThesisTriggerUtils.describe(t))
        )
      );
    }
    if (proposed.keyDataPoints !== null) {
      changes.push(
        ThesisRevisionUtils.listChange(
          'dataPoints',
          (thesis?.keyDataPoints ?? []).map(d => ThesisRevisionUtils.dataPoint(d)),
          proposed.keyDataPoints.map(d => ThesisRevisionUtils.dataPoint(d))
        )
      );
    }
    if (proposed.catalysts !== null) {
      changes.push(
        ThesisRevisionUtils.listChange(
          'catalysts',
          (thesis?.catalysts ?? []).map(c => ThesisRevisionUtils.catalyst(c)),
          proposed.catalysts.map(c => ThesisRevisionUtils.catalyst(c))
        )
      );
    }
    if (proposed.narrativeFalsifiers !== null) {
      changes.push(
        ThesisRevisionUtils.listChange(
          'falsifiers',
          thesis?.narrativeFalsifiers ?? [],
          proposed.narrativeFalsifiers
        )
      );
    }
    if (proposed.entryPrice !== null) {
      changes.push(
        ThesisRevisionUtils.textChange(
          'entryPrice',
          thesis?.entryPrice == null ? null : MoneyUtils.format(thesis.entryPrice),
          MoneyUtils.format(proposed.entryPrice)
        )
      );
    }
    return changes;
  }

  /** A promotion that proposes no text or triggers of its own and so takes the scorecard defaults. */
  public static usesDefaults(revision: ThesisRevisionDto): boolean {
    const {proposed} = revision;
    return (
      revision.kind === 'Promote' &&
      proposed.thesisText === null &&
      proposed.invalidationTriggers === null
    );
  }

  private static textChange(
    field: ThesisRevisionChangeField,
    before: Nullable<string>,
    after: string
  ): ThesisRevisionChange {
    return {
      field,
      label: THESIS_REVISION_CHANGE_LABEL[field],
      shape: 'text',
      before,
      after,
      added: [],
      removed: [],
      kept: [],
    };
  }

  private static listChange(
    field: ThesisRevisionChangeField,
    before: string[],
    after: string[]
  ): ThesisRevisionChange {
    return {
      field,
      label: THESIS_REVISION_CHANGE_LABEL[field],
      shape: 'list',
      before: null,
      after: null,
      added: after.filter(item => !before.includes(item)),
      removed: before.filter(item => !after.includes(item)),
      kept: after.filter(item => before.includes(item)),
    };
  }

  private static dataPoint(point: ThesisDataPoint): string {
    const sign = point.direction === LESS_THAN ? '<' : '>';
    const base = `${point.metric} ${sign} ${point.threshold}`;
    return point.source ? `${base} (${point.source})` : base;
  }

  private static catalyst(catalyst: ThesisCatalyst): string {
    return `${catalyst.date} · ${catalyst.event}`;
  }
}
