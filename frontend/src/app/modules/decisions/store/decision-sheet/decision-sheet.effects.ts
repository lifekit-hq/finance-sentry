import {Clipboard} from '@angular/cdk/clipboard';
import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA, CmnDrawerRef, type Nullable, ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, tap} from 'rxjs';

import {
  type DecisionSheetData,
  type RecommendationDto,
} from '../../../../shared/models/recommendation/recommendation.model';
import {ORDER_COPIED_MESSAGE} from '../../constants/recommendation/recommendation.constants';
import {RecommendationService} from '../../services/recommendation.service';

interface EffectsStore {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
}

/**
 * Records the owner's verdict and closes the sheet with the decided proposal. Confirm only records it:
 * nothing here reaches a broker, the owner places any order at IBKR from the copied text.
 */
export function decisionSheetEffects(store: EffectsStore) {
  const service = inject(RecommendationService);
  const toast = inject(ToastService);
  const clipboard = inject(Clipboard);
  const drawerRef = inject<CmnDrawerRef<RecommendationDto>>(CmnDrawerRef);
  const data = inject<DecisionSheetData>(CMN_DRAWER_DATA);

  const submit = rxMethod<string>(
    pipe(
      tap(() => store.setLoading()),
      exhaustMap(text => {
        const trimmed = text.trim();
        const request$ =
          data.verdict === 'confirm'
            ? service.confirm(data.recommendation.id, {note: trimmed || null})
            : service.reject(data.recommendation.id, {reason: trimmed});
        return request$.pipe(
          tap(decided => {
            store.setSuccess();
            toast.success(
              data.verdict === 'confirm'
                ? `Decision recorded. Place the ${decided.ticker} order at IBKR yourself.`
                : `Rejection recorded for ${decided.ticker}.`
            );
            drawerRef.close(decided);
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        );
      })
    )
  );

  return {
    submit,
    copyOrder(): void {
      const text = data.recommendation.orderText;
      if (text && clipboard.copy(text)) {
        toast.success(ORDER_COPIED_MESSAGE);
      }
    },
  };
}
