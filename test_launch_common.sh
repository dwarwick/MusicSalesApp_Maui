#!/bin/bash

# Shared helpers for the launch-crash gates (test_sims.sh, test_device.sh).
# Sourced, never executed directly.
#
# The point of this file is that all gates score logs with the SAME calibrated
# patterns. If you loosen a pattern here, loosen it everywhere at once.

# Patterns that mean "this launch crashed". Calibrated against real healthy logs:
# a clean run contains ~250 lines matching a bare `System.*Exception:` (SignalR
# socket reconnects logged at Warning), so a generic exception pattern would fail
# every device. These patterns match zero times in a healthy run.
CRASH_RE='Unhandled managed exception|Failed to lookup the required marshalling information|Native Crash Reporting|Got a SIG[A-Z]+ while executing native code|\* Assertion at|Terminating app due to uncaught exception|abort\(\) called|SIGABRT|SIGSEGV|SIGBUS|SIGILL|EXC_BAD_ACCESS|EXC_CRASH|FBSOpenApplicationErrorDomain|MonoTouchException|Fatal error'

# The app's own rolling log additionally fails on Error/Critical. [Warning] is
# deliberately NOT a failure - a healthy run logs ~20 of them (billing state,
# playback queue rebuilds). Note also the filter in
# PlaybackDiagnosticsLoggerFilter.ShouldLog: everything at Warning and above is
# written, but below Warning only allow-listed categories are, so a MISSING
# Information line proves nothing.
APP_LOG_RE="$CRASH_RE"'|\[(Error|Critical)\]'

# Android's runtime reports crashes differently from Mono/iOS. These are only
# ever applied to output already scoped to our own process (logcat --pid), never
# to the whole system log: a bare `has died` matches ActivityManager reaping
# unrelated system processes (Maps, GMS, bluetooth) constantly, and an unscoped
# `beginning of crash` matches the crash buffer's own header. Both produced
# nothing but false positives when applied system-wide.
ANDROID_CRASH_RE="$CRASH_RE"'|FATAL EXCEPTION|E AndroidRuntime|Fatal signal [0-9]+|art::Runtime::Abort|Force finishing activity'

# Applied to the unscoped system log, where only our own package matters. The
# caller substitutes the package name.
ANDROID_PACKAGE_DEATH_TEMPLATE='(Process %s .*has died|ANR in %s|FATAL EXCEPTION.*%s)'

# Google Play Billing cannot work on a locally-signed build - Play only answers
# for a build it recognises, installed from a track - so billing failures on an
# emulator are expected and must not be scored as launch crashes. See CLAUDE.md,
# "Reading device logs", for the incident where this exact noise produced a wrong
# diagnosis.
ANDROID_EXCLUDE_RE='BillingClient|GooglePlayBilling|Google Play Billing|BillingConnectionGate|IN_APP_BILLING|com\.android\.vending'

slug() {
  printf '%s' "$1" | tr -cs 'A-Za-z0-9._-' '_' | sed 's/_$//'
}

# macOS has no timeout(1); this is the equivalent.
run_with_timeout() {
  local secs="$1"; shift
  "$@" &
  local cmd_pid=$!
  ( sleep "$secs"; kill -TERM "$cmd_pid" 2>/dev/null ) &
  local killer=$!
  local rc=0
  wait "$cmd_pid" || rc=$?
  kill -TERM "$killer" 2>/dev/null || true
  wait "$killer" 2>/dev/null || true
  return "$rc"
}

# Appends matches to the findings file and echoes the hit count.
# Optional 5th argument: a regex whose matches are discarded as known-benign
# noise (see ANDROID_EXCLUDE_RE).
scan_log() {
  local file="$1" regex="$2" label="$3" findings="$4" exclude="${5:-}" hits

  [[ -s "$file" ]] || { printf '0\n'; return 0; }

  hits="$(grep -nE "$regex" "$file" 2>/dev/null || true)"
  if [[ -n "$hits" && -n "$exclude" ]]; then
    hits="$(printf '%s\n' "$hits" | grep -vE "$exclude" || true)"
  fi
  [[ -n "$hits" ]] || { printf '0\n'; return 0; }

  {
    printf '\n--- %s ---\n' "$label"
    printf '%s\n' "$hits" | head -50
  } >>"$findings"

  printf '%s\n' "$hits" | wc -l | tr -d ' '
}

# result.env is sourced by the summary pass, so the free-text field is escaped.
write_result() {
  printf 'status=%s\nfailures=%s\nlaunches=%s\nreasons=%q\n' "$2" "$3" "$4" "$5" >"$1/result.env"
}

print_gate_notes() {
  printf '\nNotes:\n'
  printf '  - Absence of a crash report is not evidence of no crash; process death\n'
  printf '    and captured stdout/stderr are the primary signals.\n'
  printf '  - In the app log, Warning+ is reliable but Information is filtered by\n'
  printf '    PlaybackDiagnosticsLoggerFilter, so missing Information lines prove nothing.\n'
}
