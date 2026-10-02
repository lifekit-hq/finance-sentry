import {Pipe, type PipeTransform} from '@angular/core';

import {MarkdownUtils} from '../utils/markdown.utils';

@Pipe({name: 'markdown'})
export class MarkdownPipe implements PipeTransform {
  public transform(source: string): string {
    return MarkdownUtils.toSafeHtml(source);
  }
}
