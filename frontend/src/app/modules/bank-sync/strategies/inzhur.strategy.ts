import {inject, Injectable, type Type} from '@angular/core';
import {map, type Observable} from 'rxjs';

import {type Provider} from '../../../shared/models/provider/provider.model';
import {InzhurFormComponent} from '../components/connect-modal/inzhur-form.component';
import {type ConnectInzhurSessionRequest} from '../models/inzhur/inzhur.model';
import {InzhurService} from '../services/inzhur.service';
import {type ConnectOutcome, type ConnectStrategy} from './connect-strategy';

/** Hands the pasted Inzhur session to the backend, which proves it with one refresh. */
@Injectable({providedIn: 'root'})
export class InzhurConnectStrategy implements ConnectStrategy {
  private readonly inzhur = inject(InzhurService);

  public readonly slug: Provider = 'inzhur';
  public readonly formComponent: Type<unknown> = InzhurFormComponent;

  public submit(input: unknown): Observable<ConnectOutcome> {
    return this.inzhur.connectSession(input as ConnectInzhurSessionRequest).pipe(
      map(() => ({
        successCode: 'CONNECTED' as const,
        count: 0,
        institutionType: 'broker' as const,
      }))
    );
  }
}
