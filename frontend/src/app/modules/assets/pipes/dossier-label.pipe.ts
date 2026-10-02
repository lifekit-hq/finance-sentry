import {Pipe, type PipeTransform} from '@angular/core';

import {DossierLabelUtils} from '../utils/dossier-label.utils';

@Pipe({name: 'coverageLabel'})
export class CoverageLabelPipe implements PipeTransform {
  public transform(code: string): string {
    return DossierLabelUtils.coverage(code);
  }
}

@Pipe({name: 'signalTypeLabel'})
export class SignalTypeLabelPipe implements PipeTransform {
  public transform(code: string): string {
    return DossierLabelUtils.signalType(code);
  }
}
