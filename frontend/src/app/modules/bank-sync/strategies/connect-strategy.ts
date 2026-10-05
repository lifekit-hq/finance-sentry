import {type Type} from '@angular/core';
import {type Observable} from 'rxjs';

import {type InstitutionType, type Provider} from '../../../shared/models/provider/provider.model';

export interface ConnectOutcome {
  readonly successCode: 'CONNECTED' | 'POLLING';
  readonly count: number;
  readonly institutionType: InstitutionType;
  readonly importPending?: boolean;
}

export interface ConnectStrategy {
  readonly slug: Provider;
  readonly formComponent: Type<unknown>;
  submit(input: unknown): Observable<ConnectOutcome>;
}
