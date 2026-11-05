import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type UniverseScreen} from '../models/universe-screen/universe-screen.model';

@Injectable({providedIn: 'root'})
export class PortfolioConstructionService extends ApiService {
  constructor() {
    super('portfolio-construction');
  }

  /** The ranked universe with the archetype's suggested weights; no archetype asks for the configured default. */
  public getUniverse(archetype?: string): Observable<UniverseScreen> {
    return this.get<UniverseScreen>('universe', archetype ? {archetype} : undefined);
  }
}
