import {inject, Injectable, type Type} from '@angular/core';
import {map, type Observable} from 'rxjs';

import {type Provider} from '../../../shared/models/provider/provider.model';
import {IbkrFormComponent} from '../components/connect-modal/ibkr-form.component';
import {type IbkrConnectInput} from '../models/ibkr/ibkr.model';
import {IBKRService} from '../services/ibkr.service';
import {type ConnectOutcome, type ConnectStrategy} from './connect-strategy';

@Injectable({providedIn: 'root'})
export class IbkrConnectStrategy implements ConnectStrategy {
  private readonly ibkr = inject(IBKRService);

  public readonly slug: Provider = 'ibkr';
  public readonly formComponent: Type<unknown> = IbkrFormComponent;

  public submit(input: unknown): Observable<ConnectOutcome> {
    const {kind, payload} = input as IbkrConnectInput;
    if (kind === 'flex') {
      // The Flex credential is saved only; holdings arrive with the daily Flex sync.
      return this.ibkr.connectFlex(payload).pipe(
        map(() => ({
          successCode: 'CONNECTED' as const,
          count: 0,
          institutionType: 'broker' as const,
          importPending: true,
        }))
      );
    }
    return this.ibkr.connect(payload).pipe(
      map(result => ({
        successCode: 'CONNECTED' as const,
        count: result.holdingsCount,
        institutionType: 'broker' as const,
      }))
    );
  }
}
