import {type TagVariant} from '@lifekit-hq/ui';

import {type PersonStatus} from '../../models/person/person.model';

export const PERSON_STATUS_TAG_VARIANT: Readonly<Record<PersonStatus, TagVariant>> = {
  /* eslint-disable @typescript-eslint/naming-convention -- keys are the API's status values */
  Invited: 'info',
  Active: 'success',
  Revoked: 'neutral',
  /* eslint-enable @typescript-eslint/naming-convention */
};
