import {type PaletteEntities} from '../models/palette-entities.model';

export interface PaletteEntitiesState {
  entities: PaletteEntities;
}

export const initialPaletteEntitiesState: PaletteEntitiesState = {
  entities: {holdings: [], watchlist: [], accounts: []},
};
