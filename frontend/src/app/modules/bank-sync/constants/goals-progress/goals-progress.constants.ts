/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type TagVariant} from '@lifekit-hq/ui';

import {type GoalTrack} from '../../models/goals-progress/goals-progress.model';

/** The typed reasons goal progress withholds a figure for (backend `GoalsProgressReasons`). */
export const GOALS_PROGRESS_REASONS = {
  noSnapshot: 'NO_SNAPSHOT',
  insufficientMonths: 'INSUFFICIENT_MONTHS',
  targetDatePassed: 'TARGET_DATE_PASSED',
  targetDateTooFar: 'TARGET_DATE_TOO_FAR',
  unrecognizedCadence: 'UNRECOGNIZED_CADENCE',
} as const;

export const GOALS_PROGRESS_ERROR_MESSAGE = 'Failed to load goal progress.';

export const GOALS_PROGRESS_EMPTY_MESSAGE = 'No goals yet';

export const GOALS_PROGRESS_EMPTY_SUB_MESSAGE =
  'Goals live on your Investment Policy Statement. To add one, ask the agent to save it to your ' +
  'IPS (the save_ips tool); there is no in-app goal editor yet.';

/** What every goal is measured against; per-goal earmarking is deliberately out of scope. */
export const GOALS_PROGRESS_BASIS =
  'Each goal is measured against your whole net worth plus your savings, not against money set ' +
  'aside for it, so goals are not additive.';

export const GOAL_TRACK_LABEL: Readonly<Record<GoalTrack, string>> = {
  OnTrack: 'On track',
  Behind: 'Behind',
  NoTargetDate: 'No target date',
  NoTargetAmount: 'No target amount',
  InsufficientHistory: 'Not enough history',
};

/** Behind is a warning, never an error colour: the card is context, not an alarm. */
export const GOAL_TRACK_VARIANT: Readonly<Record<GoalTrack, TagVariant>> = {
  OnTrack: 'success',
  Behind: 'warning',
  NoTargetDate: 'neutral',
  NoTargetAmount: 'neutral',
  InsufficientHistory: 'neutral',
};
