import {
  ACCEPT_INVITE_TOKEN_PARAM,
  ACCEPT_INVITE_USER_PARAM,
  AppRoute,
} from '../../../shared/enums/app-route/app-route.enum';

export class InviteLinkUtils {
  /** The one-time link the owner sends by hand: the accept-invite page with the user id and token. */
  public static build(origin: string, userId: string, token: string): string {
    const url = new URL(AppRoute.AcceptInvite, origin);
    url.searchParams.set(ACCEPT_INVITE_USER_PARAM, userId);
    url.searchParams.set(ACCEPT_INVITE_TOKEN_PARAM, token);
    return url.toString();
  }
}
