import {Pipe, type PipeTransform} from '@angular/core';

import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {type OutcomeTone, RecommendationUtils} from '../utils/recommendation.utils';

@Pipe({name: 'recommendationSize'})
export class RecommendationSizePipe implements PipeTransform {
  public transform(rec: RecommendationDto): string {
    return RecommendationUtils.sizeLine(rec);
  }
}

@Pipe({name: 'recommendationOutcome'})
export class RecommendationOutcomePipe implements PipeTransform {
  public transform(rec: RecommendationDto): string {
    return RecommendationUtils.outcome(rec);
  }
}

@Pipe({name: 'recommendationExcess'})
export class RecommendationExcessPipe implements PipeTransform {
  public transform(rec: RecommendationDto): string {
    return RecommendationUtils.excessLine(rec);
  }
}

@Pipe({name: 'recommendationExcessTone'})
export class RecommendationExcessTonePipe implements PipeTransform {
  public transform(rec: RecommendationDto): Nullable<OutcomeTone> {
    return RecommendationUtils.excessTone(rec);
  }
}

@Pipe({name: 'orderLines'})
export class OrderLinesPipe implements PipeTransform {
  public transform(orderText: Nullable<string>): string[] {
    return RecommendationUtils.orderLines(orderText);
  }
}
