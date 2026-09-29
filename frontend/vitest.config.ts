import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Concurrent browser workers make Vite re-optimize deps mid-run, which can deadlock the
    // runner or get it OOM-killed (exit 137) in CI. Serial files finish in under a minute.
    fileParallelism: false,
  },
});
