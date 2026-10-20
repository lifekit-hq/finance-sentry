import {DecimalPipe} from '@angular/common';
import {Pipe, type PipeTransform} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

const DEFAULT_DIGITS_INFO = '1.2-2';

@Pipe({name: 'appDecimal'})
export class AppDecimalPipe extends DecimalPipe implements PipeTransform {
  public override transform(
    value: number | string,
    digitsInfo?: string,
    locale?: string
  ): Nullable<string>;
  public override transform(value: null | undefined, digitsInfo?: string, locale?: string): null;
  public override transform(
    value: number | string | null | undefined,
    digitsInfo?: string,
    locale?: string
  ): Nullable<string>;
  public override transform(
    value: number | string | null | undefined,
    digitsInfo?: string,
    locale?: string
  ): Nullable<string> {
    return super.transform(value as number, digitsInfo ?? DEFAULT_DIGITS_INFO, locale);
  }
}
