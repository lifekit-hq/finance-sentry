import {CMN_MEDIA_MD} from '@lifekit-hq/ui';

export class DialogUtils {
  /**
   * The title for a confirm's dialog config: an action sheet draws it, while the confirm body
   * already carries its own heading wherever the dialog is a centred window (md and up).
   */
  public static phoneTitle(title: string): string | undefined {
    return window.matchMedia(CMN_MEDIA_MD).matches ? undefined : title;
  }
}
