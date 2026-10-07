#!/bin/sh
# Run "$@" while holding one of two shared "heavy validation job" slots, so at
# most 2 memory-capped validation containers run at once on the box (limit L1 of
# the compute audit). Slots are flock files under $NM_HEAVY_LOCK_DIR (default
# /tmp, which both homes share); the lock is released when the command exits.
# Waits (polling every 5 s) while both slots are taken.
dir=${NM_HEAVY_LOCK_DIR:-/tmp}
for n in 1 2; do
  (umask 0; : >> "$dir/nm-heavy.$n.lock")
done
while :; do
  for n in 1 2; do
    exec 9> "$dir/nm-heavy.$n.lock"
    if flock -n 9; then
      "$@"
      exit $?
    fi
  done
  sleep 5
done
