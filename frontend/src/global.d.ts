/* App-wide ambient types. The @lifekit-hq/ui type rollup uses these same names
   but ng-packagr drops `declare global` blocks from published .d.ts (tracked as
   lifekit-common bug) — so the app declares them itself. */
declare global {
  type Nullable<T> = T | null;

  type AsyncStatus = 'idle' | 'loading' | 'error';
}

export {};
