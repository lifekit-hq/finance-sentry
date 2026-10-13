import {Pipe, type PipeTransform} from '@angular/core';

import {DossierLabelUtils} from '../utils/dossier-label.utils';
import {DossierViewUtils} from '../utils/dossier-view.utils';

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

@Pipe({name: 'analystActionLabel'})
export class AnalystActionLabelPipe implements PipeTransform {
  public transform(code: string): string {
    return DossierLabelUtils.analystAction(code);
  }
}

@Pipe({name: 'newsSourceLabel'})
export class NewsSourceLabelPipe implements PipeTransform {
  public transform(source: string): string {
    return DossierLabelUtils.newsSource(source);
  }
}

@Pipe({name: 'peerSetLabel'})
export class PeerSetLabelPipe implements PipeTransform {
  public transform(name: string): string {
    return DossierLabelUtils.peerSet(name);
  }
}

@Pipe({name: 'dividendYield'})
export class DividendYieldPipe implements PipeTransform {
  public transform(fraction: Nullable<number>): Nullable<string> {
    return DossierViewUtils.dividendYield(fraction);
  }
}
