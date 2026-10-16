import {TAB_PRELOAD_DATA_KEY} from '@lifekit-hq/core';

/** Route data that lets `TabPreloadStrategy` load a bottom tab's chunks on an idle moment. */
export const TAB_PRELOAD_DATA = {[TAB_PRELOAD_DATA_KEY]: true} as const;
