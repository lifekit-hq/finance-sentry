import {computed, type Signal} from '@angular/core';

import {type CategoryModel} from '../../models/category/category.model';

interface ComputedStore {
  categories: Signal<CategoryModel[]>;
}

export function categoriesComputed(store: ComputedStore) {
  return {
    labelMap: computed(() => {
      const map: Record<string, string> = {};
      for (const category of store.categories()) {
        map[category.key] = category.label;
      }
      return map;
    }),
    filterOptions: computed(() =>
      store.categories().map(category => ({value: category.key, label: category.label}))
    ),
  };
}
