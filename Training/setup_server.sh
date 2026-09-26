#!/usr/bin/env bash
# =============================================================================
#  One-shot setup for a fresh Jarvis Labs box (PyTorch template, Ubuntu 22.04).
#
#  Run this FIRST on any new instance, before train_16.sh. It rebuilds the exact
#  environment that was validated on 2026-08-31, because the conda env lives under
#  /root, which Jarvis Labs wipes on both pause and destroy -- only /home survives.
#
#  USAGE (from your laptop, after uploading Training/ to /home):
#      ssh -i ~/.ssh/jarvislabs root@<NEW_IP> "bash /home/Training/setup_server.sh"
#
#  Takes roughly 5-10 minutes, almost all of it downloading torch.
#
#  EVERY VERSION BELOW IS PINNED FOR A REASON -- each one was a real failure that
#  cost time to diagnose. Do not "modernise" them:
#
#   * python 3.10.12  - mlagents 1.1.0 declares Requires-Python >=3.10.1,<=3.10.12.
#                       The box ships 3.10.20, which pip REFUSES outright. Built
#                       from conda-forge, not defaults, so no Anaconda ToS prompt.
#   * setuptools <81  - setuptools 81 removed pkg_resources, which mlagents'
#                       torch_utils still imports -> ModuleNotFoundError on startup.
#   * torch 2.2.2     - torch 2.13's ONNX exporter imports `onnxscript`, which in
#                       turn needs numpy >= 1.25, which mlagents forbids. Installing
#                       onnxscript silently dragged numpy to 2.2.6, protobuf to 6.x
#                       and onnx to 1.22, breaking mlagents completely. torch 2.2.2
#                       uses the legacy exporter and needs none of that.
#   * numpy 1.23.5    - mlagents needs <1.24 (it still calls the removed np.float).
#   * protobuf 3.20.3 - mlagents' generated _pb2 files fail on protobuf >= 3.21.
#   * onnx 1.15.0     - what mlagents 1.1.0 pins.
#
#  System packages: xvfb is REQUIRED, not optional. The Unity build segfaults under
#  -nographics (Unity 6's Null graphics device cannot serve this URP project), so
#  train_16.sh runs it against a tiny virtual display instead.
# =============================================================================

set -euo pipefail

ENV_NAME="${ENV_NAME:-mla}"
ENV_BIN="/root/miniconda3/envs/$ENV_NAME/bin"

echo "==> 1/5  system packages (xvfb is required; the build cannot run headless)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq xvfb libgtk-3-0 libnss3 libasound2

echo "==> 2/5  conda env at python 3.10.12 (conda-forge, avoids the Anaconda ToS prompt)"
conda create -y -q -n "$ENV_NAME" python=3.10.12 -c conda-forge --override-channels

# conda-forge's python package does not bundle pip.
"$ENV_BIN/python" -m ensurepip --upgrade >/dev/null

echo "==> 3/5  mlagents 1.1.0 (pulls its own torch, which we replace next)"
"$ENV_BIN/python" -m pip install -q mlagents==1.1.0

echo "==> 4/5  correcting the versions mlagents' own install breaks"
# mlagents pulls setuptools 84 (no pkg_resources) and torch 2.13 (needs onnxscript).
"$ENV_BIN/python" -m pip install -q "setuptools<81" "torch==2.2.2"
# Reassert the pins in case the torch install moved them.
"$ENV_BIN/python" -m pip install -q "numpy==1.23.5" "protobuf<3.21" "onnx==1.15.0"

echo "==> 5/5  verifying"
"$ENV_BIN/python" - <<'PY'
import numpy, onnx, torch, google.protobuf, setuptools, pkg_resources  # noqa: F401
print("  python      :", __import__("sys").version.split()[0])
print("  torch       :", torch.__version__, "| cuda:", torch.cuda.is_available())
print("  numpy       :", numpy.__version__)
print("  protobuf    :", google.protobuf.__version__)
print("  onnx        :", onnx.__version__)
print("  setuptools  :", setuptools.__version__, "(pkg_resources importable)")
PY

# The real proof: the trainer's entry point must actually start. An import-time
# version clash shows up here and nowhere earlier.
"$ENV_BIN/mlagents-learn" --help >/dev/null 2>&1 \
    && echo "  mlagents-learn: OK" \
    || { echo "  mlagents-learn: FAILED to start"; exit 1; }

chmod +x /home/Training/build/Hummingbird.x86_64 2>/dev/null || true

cat <<EOF

  Setup complete.

  Add the env to PATH, then train:
      export PATH=$ENV_BIN:\$PATH
      cd /home/Training
      ./train_16.sh smoke      # sanity check, ~10 min
      ./train_16.sh            # the real run (28 islands x 4 procs, measured fastest)

  Always run inside tmux so an SSH drop does not kill a paid run:
      tmux new -s hb

EOF
