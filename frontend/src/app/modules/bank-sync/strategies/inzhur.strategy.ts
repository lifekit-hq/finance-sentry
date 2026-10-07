import {inject, Injectable, type Type} from '@angular/core';
import {type Observable, of, switchMap, throwError} from 'rxjs';

import {type Provider} from '../../../shared/models/provider/provider.model';
import {InzhurFormComponent} from '../components/connect-modal/inzhur-form.component';
import {INZHUR_INVALID_CODE} from '../constants/inzhur/inzhur.constants';
import {type VerifyInzhurLoginRequest} from '../models/inzhur/inzhur.model';
import {InzhurService} from '../services/inzhur.service';
import {type ConnectOutcome, type ConnectStrategy} from './connect-strategy';

/**
 * Submits the SMS code: the last step of an Inzhur sign-in. The first step (phone + password,
 * which sends the SMS) runs in the form's InzhurConnectStore.
 */
@Injectable({providedIn: 'root'})
export class InzhurConnectStrategy implements ConnectStrategy {
  private readonly inzhur = inject(InzhurService);

  public readonly slug: Provider = 'inzhur';
  public readonly formComponent: Type<unknown> = InzhurFormComponent;

  public submit(input: unknown): Observable<ConnectOutcome> {
    return this.inzhur.verifyLogin(input as VerifyInzhurLoginRequest).pipe(
      switchMap(result =>
        result.status === 'connected'
          ? of({successCode: 'CONNECTED' as const, count: 0, institutionType: 'broker' as const})
          : throwError(() => ({
              errorCode: INZHUR_INVALID_CODE,
              attemptsLeft: result.attemptsLeft,
            }))
      )
    );
  }
}
