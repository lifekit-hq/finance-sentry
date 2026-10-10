import {inject, Injectable} from '@angular/core';
import {Title} from '@angular/platform-browser';
import {type RouterStateSnapshot, TitleStrategy} from '@angular/router';

import {APP_NAME, TAB_TITLE_SEPARATOR} from './app-title.constants';

/** Sets the browser tab to "<Page> · Finance Sentry" from the deepest `Route.title`; the app name alone without one. */
@Injectable({providedIn: 'root'})
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  public override updateTitle(snapshot: RouterStateSnapshot): void {
    const pageTitle = this.buildTitle(snapshot);
    this.title.setTitle(pageTitle ? `${pageTitle}${TAB_TITLE_SEPARATOR}${APP_NAME}` : APP_NAME);
  }
}
