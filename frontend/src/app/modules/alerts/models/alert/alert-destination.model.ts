import {type AppRoute} from '../../../../shared/enums/app-route/app-route.enum';

/** Where tapping an alert goes: an app page, the asset dossier of the alert's ticker, or nowhere. */
export type AlertDestination = AppRoute | 'dossier' | 'none';

/** A resolved tap target — an in-app router command list, an in-app URL with its query string, or an external document. */
export type AlertNavigation =
  | {kind: 'route'; commands: string[]}
  | {kind: 'url'; url: string}
  | {kind: 'external'; url: string};
