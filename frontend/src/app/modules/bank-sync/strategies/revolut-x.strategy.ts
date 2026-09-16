import {inject, Injectable, type Type} from '@angular/core';
import {map, type Observable} from 'rxjs';

import {type Provider} from '../../../shared/models/provider/provider.model';
import {RevolutXFormComponent} from '../components/connect-modal/revolut-x-form.component';
import {type ConnectRevolutXRequest} from '../models/revolut-x/revolut-x.model';
import {RevolutXService} from '../services/revolut-x.service';
import {type ConnectOutcome, type ConnectStrategy} from './connect-strategy';

@Injectable({providedIn: 'root'})
export class RevolutXConnectStrategy implements ConnectStrategy {
  private readonly revolutX = inject(RevolutXService);

  public readonly slug: Provider = 'revolut_x';
  public readonly formComponent: Type<unknown> = RevolutXFormComponent;

  public submit(input: unknown): Observable<ConnectOutcome> {
    const payload = input as ConnectRevolutXRequest;
    return this.revolutX.connect(payload).pipe(
      map(() => ({
        successCode: 'POLLING' as const,
        count: 1,
        institutionType: 'crypto' as const,
      }))
    );
  }
}
