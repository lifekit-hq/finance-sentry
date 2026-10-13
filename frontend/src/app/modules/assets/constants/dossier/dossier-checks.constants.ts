import {type DossierCheckAxisKey} from '../../models/dossier/dossier.model';

/** Every axis of the summary wheel carries this many named rules. */
export const RULES_PER_AXIS = 2;

/**
 * The wheel's SVG viewBox edge and the radius its spokes reach; labels sit outside the radius, and
 * the edge leaves room for the longest ("Momentum", "Growth") beside a slanted spoke.
 */
export const WHEEL_SIZE = 260;
export const WHEEL_RADIUS = 70;
export const WHEEL_LABEL_OFFSET = 16;

/** The rule checks behind each question-led section's tick strip. */
export const SECTION_AXIS: Readonly<Record<string, DossierCheckAxisKey>> = {
  chart: 'momentum',
  valuation: 'value',
  analysts: 'analysts',
  thesis: 'fit',
};
