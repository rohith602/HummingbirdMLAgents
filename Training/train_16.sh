#!/usr/bin/env bash
# =============================================================================
#  Hummingbird PPO -- multi-island training run, for a rented Linux GPU box.
#
#  Goal: beat run HB_01's final mean reward of 41.29.
#
#  Target box (chosen 2026-08-31): Jarvis Labs L4, 28 vCPU / 124GB RAM,
#  Rs 41.31/hr + Rs 1.30/hr for 100GB storage. NOT the A30 in the older notes --
#  L4 bundles 28 vCPU against A30's 16 for ~3.5% more money, and this workload is
#  CPU-bound (HB_01 spent 95.5% of wall clock in Unity physics, 3.5% on gradients),
#  so vCPU count is what actually decides throughput.
#
#  USAGE, in the order you actually run them
#    ./train_16.sh smoke      # 1. does it run at all? ~10 min, watch it.
#    ./train_16.sh sweep      # 2. what is the FASTEST config? ~25 min, automatic.
#    ./train_16.sh            # 3. the real run. Auto-resumes if interrupted.
#    ./train_16.sh status     # any time, in a SECOND terminal, while training runs.
#
#  Every setting below can be overridden on the command line without editing
#  this file, e.g.:   DEVICE=cpu NUM_ENVS=2 ./train_16.sh smoke
#
#  The arena count is NOT baked into the build -- IslandSpawner.cs reads --islands
#  at startup -- so trying a different layout costs a re-run, not a rebuild and a
#  multi-GB re-upload. `sweep` exploits that: it measures every split of the same
#  arena count across processes and tells you which is fastest. Prefer it over
#  guessing, because the answer is genuinely not predictable (see the note above
#  the sweep block: Unity's main loop is single-threaded per process).
#
#  Expected folder layout on the server (upload the whole Training/ folder):
#    Training/
#      train_16.sh
#      config/trainer_config_16.yaml
#      build/Hummingbird.x86_64      <- the headless Linux build of the Unity scene
#      results/                      <- created for you
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Knobs. `${X:-default}` means "use the value of X if it was set outside this
# script, otherwise use the default".
# ---------------------------------------------------------------------------
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

RUN_ID="${RUN_ID:-HB_16}"
CONFIG="${CONFIG:-$HERE/config/trainer_config_16.yaml}"
ENV_PATH="${ENV_PATH:-$HERE/build/Hummingbird.x86_64}"
RESULTS_DIR="${RESULTS_DIR:-$HERE/results}"
# MEASURED on the L4 (28 vCPU) 2026-08-31, not guessed. 28 islands x 4 processes
# = 112 arenas ran fastest at 3237 steps/sec. Full sweep, same 28 total arenas:
#   7x4 2665 | 14x2 2247 | 4x7 2231 | 28x1 2014 | 2x14 1472 | 1x28 873
# then scaling the total arena count up:
#   28x4 (112) 3237 | 14x8 (112) 3106 | 14x4 (56) 2892 | 7x8 (56) 2880 | 7x4 (28) 2642
# Note the flattening: 4x the arenas bought only +22% throughput, so 112 is about
# the practical ceiling for this box -- the CPU is saturated past that.
ISLANDS="${ISLANDS:-28}"           # arenas per Unity process, read by IslandSpawner.cs
NUM_ENVS="${NUM_ENVS:-4}"          # Unity processes (total arenas = ISLANDS x NUM_ENVS).
                                   # Unity's main loop is single-threaded per process,
                                   # so neither extreme wins -- see the numbers above.
DEVICE="${DEVICE:-cuda}"           # cuda | cpu  -- measure both in the smoke test
TIME_SCALE="${TIME_SCALE:-20}"
RATE_PER_HOUR="${RATE_PER_HOUR:-42.61}"   # rupees/hour, only used for the cost estimate.
                                          # L4 compute 41.31 + 100GB storage 1.30.
BASELINE_REWARD="41.29"                   # HB_01's final mean reward -- the number to beat

MODE="${1:-run}"
if [ "$MODE" = "smoke" ]; then
    RUN_ID="${RUN_ID}_smoke"
fi

LOG="$RESULTS_DIR/${RUN_ID}_console.log"

die() { echo "ERROR: $*" >&2; exit 1; }

# ---------------------------------------------------------------------------
# `status` -- parse the console log we tee'd and report progress.
# Reads the log rather than the checkpoint files, because the log updates every
# summary_freq (~2 min) whereas checkpoints are ~9 min apart, so this works even
# during a very short smoke run.
# ---------------------------------------------------------------------------
if [ "$MODE" = "status" ]; then
    python3 - "$LOG" "$CONFIG" "$RATE_PER_HOUR" "$BASELINE_REWARD" <<'PY'
import os, re, sys

log, cfg, rate_per_hour, baseline = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4])

if not os.path.exists(log):
    sys.exit("No log at %s yet -- has the run started?" % log)

max_steps = None
try:
    import yaml
    max_steps = yaml.safe_load(open(cfg))["behaviors"]["Hummingbird"]["max_steps"]
except Exception:
    pass

pat_step   = re.compile(r"Step:\s*(\d+)\.\s*Time Elapsed:\s*([0-9.]+) s")
pat_reward = re.compile(r"Mean Reward:\s*(-?[0-9]+\.[0-9]+)")

rows = []   # (step, elapsed_seconds, mean_reward_or_None)
for line in open(log, errors="ignore"):
    m = pat_step.search(line)
    if m:
        r = pat_reward.search(line)
        rows.append((int(m.group(1)), float(m.group(2)), float(r.group(1)) if r else None))

if not rows:
    sys.exit("Log exists but no progress lines yet -- give it a minute.")

step, elapsed, _ = rows[-1]
rewards = [r for _, _, r in rows if r is not None]

def hms(s):
    s = int(s)
    return "%dh %02dm" % (s // 3600, (s % 3600) // 60)

print("")
print("  run          : %s" % os.path.basename(log).replace("_console.log", ""))
print("  steps done   : {:,}{}".format(step, "  of {:,}".format(max_steps) if max_steps else ""))
print("  elapsed      : %s   (this segment only -- resets on --resume)" % hms(elapsed))

# Rate from the last few points only, so a resume or a slow start doesn't skew it.
window = rows[-6:] if len(rows) >= 6 else rows
if len(window) >= 2 and window[-1][1] > window[0][1]:
    rate = (window[-1][0] - window[0][0]) / (window[-1][1] - window[0][1])
    print("  speed        : %.0f steps/sec  (recent average)" % rate)
    if max_steps and rate > 0:
        remaining = max(0, max_steps - step) / rate
        print("  eta          : %s remaining" % hms(remaining))
        total_h = (elapsed + remaining) / 3600.0
        print("  cost         : ~Rs. %.0f for the whole run at Rs. %.2f/hr"
              % (total_h * rate_per_hour, rate_per_hour))
else:
    print("  speed        : need one more progress line to measure")

if rewards:
    print("")
    print("  mean reward  : %.2f  (latest)" % rewards[-1])
    print("  best so far  : %.2f" % max(rewards))
    delta = max(rewards) - baseline
    verdict = "BEATEN by %.2f" % delta if delta > 0 else "not yet -- %.2f to go" % -delta
    print("  vs HB_01 (%.2f) : %s" % (baseline, verdict))
print("")
PY
    exit 0
fi

# ---------------------------------------------------------------------------
# Pre-flight checks. Each of these is a real way this run can waste rented time.
# ---------------------------------------------------------------------------
command -v mlagents-learn >/dev/null 2>&1 \
    || die "mlagents-learn not found. Activate the conda env first: conda activate ml-agents-1.0"

[ -f "$CONFIG" ]   || die "config not found: $CONFIG"
[ -f "$ENV_PATH" ] || die "Unity build not found: $ENV_PATH (did you upload build/ ?)"

# Uploading a Linux build from Windows strips the executable bit, and Unity then
# fails to launch with a confusing 'permission denied'. Put it back.
[ -x "$ENV_PATH" ] || { echo "note: adding +x to $ENV_PATH"; chmod +x "$ENV_PATH"; }

# If your SSH connection drops, a foreground run dies with it and you pay for
# nothing. tmux keeps it alive.
if [ -z "${TMUX:-}" ] && [ -z "${STY:-}" ]; then
    echo ""
    echo "  WARNING: not inside tmux/screen. If your SSH drops, the run dies."
    echo "           Recommended:  tmux new -s hb   then re-run this script."
    echo ""
    sleep 5
fi

mkdir -p "$RESULTS_DIR"

# ---------------------------------------------------------------------------
# Virtual display.
#
# This build CANNOT run with -nographics: it segfaults (SIGSEGV in PlayerMain)
# the instant the scene finishes loading, because Unity 6's Null graphics device
# cannot serve this URP project. Measured on the L4 box 2026-08-31: -nographics
# crashed every time, the same binary under Xvfb ran fine.
#
# So we give it a real -- but deliberately small -- X display.
#
# The depth MUST be 24. At 8bpp Xvfb offers no GLX, and Unity then dies with
# "GLX is not supported / No supported renderers found, exiting" -- a different
# failure from the -nographics segfault, and just as fatal. 24bpp lets Mesa's
# llvmpipe software renderer load. The resolution is what we keep small, since
# fill cost is the part that scales with pixels and nothing ever looks at the
# output.
#
# One Xvfb serves every Unity process mlagents launches, because they all inherit
# DISPLAY, so this works unchanged for any --num-envs.
# ---------------------------------------------------------------------------
XVFB_DISPLAY="${XVFB_DISPLAY:-:99}"
if [ -z "${DISPLAY:-}" ]; then
    command -v Xvfb >/dev/null 2>&1 \
        || die "Xvfb not installed and DISPLAY is unset. Install it:  apt-get install -y xvfb"

    # Probe for an existing server by its lock file: xdpyinfo is not installed on
    # a bare box, so testing with it would silently always fail and start a second
    # Xvfb on a display that is already taken.
    if [ ! -e "/tmp/.X${XVFB_DISPLAY#:}-lock" ]; then
        echo "  starting Xvfb on $XVFB_DISPLAY (128x128x24)"
        Xvfb "$XVFB_DISPLAY" -screen 0 128x128x24 -nolisten tcp >/dev/null 2>&1 &
        sleep 3
    else
        echo "  reusing existing Xvfb on $XVFB_DISPLAY"
    fi
    export DISPLAY="$XVFB_DISPLAY"
fi
echo "  display   : $DISPLAY"

# Passed to every Unity process. A tiny window costs less to rasterise, and the
# rendered output is never looked at.
SCREEN_ARGS="${SCREEN_ARGS:--batchmode -screen-width 128 -screen-height 128 -screen-quality Fastest}"

# ---------------------------------------------------------------------------
# `sweep` -- find the fastest way to split N arenas across processes.
#
# WHY THIS EXISTS. Unity's main loop (FixedUpdate, and therefore every agent's
# CollectObservations/OnActionReceived) runs on ONE thread per process. So 28
# islands inside a single Unity process step sequentially on a single core, no
# matter how many cores the box has. Running 28 separate processes with 1 island
# each spreads the identical workload across 28 cores instead.
#
# Both extremes have a real cost: many processes multiply memory and startup time
# (each loads the whole scene) and add per-process trainer communication, while
# one process leaves cores idle. The best split is in between and cannot be
# predicted -- it has to be measured, which is what this does.
#
# Each combo below runs the SAME total arena count, so throughput is comparable.
# ---------------------------------------------------------------------------
if [ "$MODE" = "sweep" ]; then
    COMBOS="${COMBOS:-28:1 14:2 7:4 4:7 2:14 1:28}"
    SWEEP_SECONDS="${SWEEP_SECONDS:-240}"

    # The real config reports every 50,000 steps and checkpoints every 250,000 --
    # sensible for an 18-hour run, useless for a 4-minute one: a combo could finish
    # its slot without ever printing a single progress line, and every combo would
    # then be scored "no data". So the sweep runs against a copy with frequent
    # reporting and checkpointing pushed out of the way.
    SWEEP_CONFIG="$RESULTS_DIR/_sweep_config.yaml"
    python3 - "$CONFIG" "$SWEEP_CONFIG" <<'PY'
import sys, yaml
src, dst = sys.argv[1], sys.argv[2]
cfg = yaml.safe_load(open(src))
for name, b in cfg.get("behaviors", {}).items():
    b["summary_freq"] = 2000          # a progress line every few seconds
    b["checkpoint_interval"] = 10**9  # never checkpoint during a throwaway run
    b["max_steps"] = 10**9            # never stop early, the timer ends the run
yaml.safe_dump(cfg, open(dst, "w"), sort_keys=False)
print("  sweep config: summary_freq=2000, checkpointing disabled")
PY

    echo ""
    echo "  SWEEP -- ${SWEEP_SECONDS}s per combo, $(echo $COMBOS | wc -w) combos."
    echo "  Roughly $(( $(echo $COMBOS | wc -w) * SWEEP_SECONDS / 60 )) min total, plus Unity startup each time."
    echo ""

    for combo in $COMBOS; do
        isl="${combo%%:*}"
        ne="${combo##*:}"
        rid="sweep_${isl}x${ne}"
        slog="$RESULTS_DIR/${rid}_console.log"

        rm -rf "${RESULTS_DIR:?}/$rid"
        : > "$slog"

        echo "  -> $isl islands x $ne process(es) = $((isl * ne)) arenas ..."

        # SIGINT rather than the default SIGTERM: mlagents-learn treats Ctrl+C as a
        # clean shutdown, so the log is flushed and the ports are released before the
        # next combo starts. `|| true` because a timed-out command exits non-zero and
        # `set -e` would otherwise abandon the sweep after the first combo.
        timeout --signal=INT "${SWEEP_SECONDS}s" \
            mlagents-learn "$SWEEP_CONFIG" \
                --run-id="$rid" \
                --env="$ENV_PATH" \
                --results-dir="$RESULTS_DIR" \
                --num-envs="$ne" \
                --time-scale="$TIME_SCALE" \
                --torch-device="$DEVICE" \
                --force \
                --env-args -logFile "$RESULTS_DIR/${rid}_unity.log" --islands "$isl" $SCREEN_ARGS \
                > "$slog" 2>&1 || true

        # Let the OS reclaim ports/memory before the next combo starts.
        sleep 5
    done

    echo ""
    python3 - "$RESULTS_DIR" "$COMBOS" <<'PY'
import os, re, sys

results_dir, combos = sys.argv[1], sys.argv[2].split()
pat = re.compile(r"Step:\s*(\d+)\.\s*Time Elapsed:\s*([0-9.]+) s")

rows = []
for combo in combos:
    isl, ne = combo.split(":")
    log = os.path.join(results_dir, "sweep_%sx%s_console.log" % (isl, ne))
    pts = []
    if os.path.exists(log):
        for line in open(log, errors="ignore"):
            m = pat.search(line)
            if m:
                pts.append((int(m.group(1)), float(m.group(2))))

    # Measure from the first progress line, not from zero: the gap before it is
    # Unity startup (scene load), which is one-off and would understate a combo
    # that simply takes longer to boot.
    if len(pts) >= 2 and pts[-1][1] > pts[0][1]:
        rate = (pts[-1][0] - pts[0][0]) / (pts[-1][1] - pts[0][1])
        rows.append((rate, isl, ne, "%.0f" % rate))
    else:
        rows.append((-1.0, isl, ne, "no data (too short? check its log)"))

rows.sort(reverse=True)
print("  RESULTS (higher steps/sec is better)")
print("  %-10s %-10s %-8s %s" % ("islands", "processes", "arenas", "steps/sec"))
for rate, isl, ne, shown in rows:
    print("  %-10s %-10s %-8s %s" % (isl, ne, int(isl) * int(ne), shown))

best = rows[0]
if best[0] > 0:
    print("")
    print("  FASTEST: ISLANDS=%s NUM_ENVS=%s  at %s steps/sec" % (best[1], best[2], best[3]))
    print("  Use it for the real run:  ISLANDS=%s NUM_ENVS=%s ./train_16.sh" % (best[1], best[2]))
    print("")
    print("  Sanity-check against HB_01's ~45 steps/sec on 1 island in the Editor.")
    print("  Then set max_steps in the config from this rate and your budget:")
    print("      hours = max_steps / %s / 3600" % best[3])
PY
    echo ""
    echo "  Sweep runs are throwaway. Delete them before the real run:  rm -rf $RESULTS_DIR"
    echo ""
    exit 0
fi

# mlagents refuses to overwrite an existing run unless you say --resume.
RESUME=""
if [ -d "$RESULTS_DIR/$RUN_ID" ]; then
    RESUME="--resume"
    echo "  found existing results for '$RUN_ID' -> resuming"
fi

if [ "$MODE" = "smoke" ]; then
    cat <<EOF

  SMOKE TEST ($RUN_ID) -- $ISLANDS islands x $NUM_ENVS process(es)

  THIS run is the experiment: the point is to find how many islands this box
  actually sustains, not to confirm a number picked in advance.

  Let it run ~10 minutes, then in a second terminal:

      ./train_16.sh status          # steps/sec
      htop                          # are all 28 vCPUs busy, or is one pegged?

  Once you have seen it work, STOP GUESSING BY HAND and let the sweep measure it:

      ./train_16.sh sweep                 # ~25 min, ranks every split automatically
      DEVICE=cpu ./train_16.sh sweep      # then repeat on CPU and compare the winners

  Test cpu vs cuda too: HB_01 spent only 3.5% of wall clock on gradients, and this
  network is a 30 -> 256 -> 256 -> 5 MLP, small enough that CPU often wins outright
  once per-op GPU launch overhead is counted.

  Then set \`max_steps\` in the config from the winning steps/sec and your budget,
  delete results/, and start the real run with the winning ISLANDS and NUM_ENVS.

EOF
fi

# Print a one-line summary of what is about to happen, then run it.
echo "  run id    : $RUN_ID"
echo "  config    : $CONFIG"
echo "  env       : $ENV_PATH"
echo "  arenas    : $ISLANDS islands x $NUM_ENVS process(es) = $((ISLANDS * NUM_ENVS)) total"
echo "  device    : $DEVICE"
echo "  log       : $LOG"
echo ""
echo "  (confirm the island count actually took effect -- IslandSpawner logs it:"
echo "     grep IslandSpawner $RESULTS_DIR/${RUN_ID}_unity.log )"
echo ""

# On exit -- normal finish OR Ctrl+C -- print the progress summary automatically.
trap 'echo ""; "$0" status || true' EXIT

# NOTE: --no-graphics is deliberately NOT passed. It makes this build segfault
#       (see the Xvfb block above); the tiny Xvfb display replaces it.
# --torch-device: cpu or cuda, see the note in the config file
# --env-args    : passed straight to the Unity player; -logFile captures Unity's
#                 own crash/error output (which mlagents does not show you), and
#                 --islands is read by IslandSpawner.cs to build the arena grid.
#
# --env-args MUST BE LAST. argparse defines it with nargs=REMAINDER, so it
# swallows every argument that follows it -- putting $RESUME after it would hand
# "--resume" to the Unity player instead of to mlagents-learn, and the resume
# would silently not happen.
mlagents-learn "$CONFIG" \
    --run-id="$RUN_ID" \
    --env="$ENV_PATH" \
    --results-dir="$RESULTS_DIR" \
    --num-envs="$NUM_ENVS" \
    --time-scale="$TIME_SCALE" \
    --torch-device="$DEVICE" \
    $RESUME \
    --env-args -logFile "$RESULTS_DIR/${RUN_ID}_unity.log" --islands "$ISLANDS" $SCREEN_ARGS \
    2>&1 | tee -a "$LOG"
