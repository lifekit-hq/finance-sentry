import type {ResolveFn, Route} from '@angular/router';

/**
 * Declares a page's title once, on `Route.title`. `cmn-app-layout` reads its header title from the
 * route's `data.title`, so the same value is also resolved into that key: the tab title and the
 * header always come from this one declaration, never from a hand-written `data.title`.
 */
export class RouteTitleUtils {
  public static of(title: string | ResolveFn<string>): Pick<Route, 'title' | 'resolve'> {
    const resolver: ResolveFn<string> = typeof title === 'string' ? () => title : title;
    return {title: resolver, resolve: {title: resolver}};
  }
}
