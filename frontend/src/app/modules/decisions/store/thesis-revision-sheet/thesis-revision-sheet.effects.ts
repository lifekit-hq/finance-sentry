import {HttpErrorResponse} from '@angular/common/http';
import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA, CmnDrawerRef, type Nullable, ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, tap} from 'rxjs';

import {
  type DecideThesisRevisionResult,
  type ThesisRevisionSheetData,
  type ThesisRiskGateVerdict,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {
  THESIS_REVISION_CONFIRMED_TOAST,
  THESIS_REVISION_REJECTED_TOAST,
} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisRevisionService} from '../../services/thesis-revision.service';

interface EffectsStore {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setRefusal(refusal: Nullable<ThesisRiskGateVerdict>): void;
}

const CONFLICT_STATUS = 409;

/** The gate's refusal of a promotion answers 409 with the decision's own body; any other 409 is a plain error. */
function refusalOf(err: unknown): Nullable<ThesisRiskGateVerdict> {
  if (!(err instanceof HttpErrorResponse) || err.status !== CONFLICT_STATUS) {
    return null;
  }
  const body = err.error as Nullable<Partial<DecideThesisRevisionResult>>;
  return body?.gate ? {...body.gate, note: body.gate.note ?? body.note ?? null} : null;
}

/**
 * Records the owner's verdict and closes the sheet with the decision. Confirm writes the thesis (a promotion runs the
 * risk gate first, and a refusal leaves the proposal pending); nothing here places or changes a position.
 */
export function thesisRevisionSheetEffects(store: EffectsStore) {
  const service = inject(ThesisRevisionService);
  const toast = inject(ToastService);
  const drawerRef = inject<CmnDrawerRef<DecideThesisRevisionResult>>(CmnDrawerRef);
  const data = inject<ThesisRevisionSheetData>(CMN_DRAWER_DATA);

  const submit = rxMethod<{text: string; overrideRisk: boolean}>(
    pipe(
      tap(() => {
        store.setRefusal(null);
        store.setLoading();
      }),
      exhaustMap(({text, overrideRisk}) => {
        const trimmed = text.trim();
        const request$ =
          data.verdict === 'confirm'
            ? service.confirm(data.revision.id, {note: trimmed || null, overrideRisk})
            : service.reject(data.revision.id, {reason: trimmed});
        return request$.pipe(
          tap(result => {
            store.setSuccess();
            toast.success(
              data.verdict === 'confirm'
                ? `${THESIS_REVISION_CONFIRMED_TOAST} (${result.revision.ticker})`
                : `${THESIS_REVISION_REJECTED_TOAST} (${result.revision.ticker})`
            );
            drawerRef.close(result);
          }),
          catchError((err: unknown) => {
            const refusal = refusalOf(err);
            if (refusal) {
              store.setRefusal(refusal);
              store.setSuccess();
            } else {
              store.setError(extractErrorCode(err));
            }
            return EMPTY;
          })
        );
      })
    )
  );

  return {submit};
}
