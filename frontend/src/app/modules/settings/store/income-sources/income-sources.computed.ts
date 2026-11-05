import {computed, inject, type Signal} from '@angular/core';
import {type AsyncStatus, ErrorMessageService} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {DateUtils} from '../../../../shared/utils/date.utils';
import {
  INCOME_BASIS_LABEL,
  INCOME_CADENCE_LABEL,
  INCOME_KIND_LABEL,
} from '../../constants/income-source/income-source.constants';
import {
  type IncomeSource,
  type IncomeSourceRow,
} from '../../models/income-source/income-source.model';

interface StateSignals {
  sources: Signal<IncomeSource[]>;
  sourcesStatus: Signal<AsyncStatus>;
  actionErrorCode: Signal<Nullable<string>>;
}

function sublabel(source: IncomeSource): string {
  const parts = [
    INCOME_KIND_LABEL.get(source.kind) ?? source.kind,
    INCOME_CADENCE_LABEL.get(source.cadence) ?? source.cadence,
    INCOME_BASIS_LABEL.get(source.grossOrNet) ?? source.grossOrNet,
  ];
  if (source.endDate) {
    parts.push(`ended ${DateUtils.dayLabel(source.endDate, true)}`);
  }
  return parts.join(' · ');
}

export function incomeSourcesComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    isLoading: computed(() => store.sourcesStatus() === 'loading' && store.sources().length === 0),
    rows: computed<IncomeSourceRow[]>(() =>
      store.sources().map(source => ({...source, sublabel: sublabel(source)}))
    ),
    actionErrorMessage: computed(() => {
      const code = store.actionErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not update the income source.') : '';
    }),
  };
}
