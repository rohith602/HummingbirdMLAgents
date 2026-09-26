# Renting the box and starting the run

Written 2026-08-31. Costs are in rupees.

**The Linux build is already done** -- `build/Hummingbird.x86_64`, 154.7 MB, 195 files,
built and verified 2026-08-31. Non-development build (no profiler overhead), contains
only `Training.unity`, and verified to include the ML-Agents runtime, the native Linux
gRPC library, and `IslandSpawner`. Nothing below requires Unity.

To rebuild it later (close the Unity Editor first -- it locks the project):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe" -quit -batchmode `
    -projectPath "<path to the Unity project>" `
    -executeMethod BuildLinuxHeadless.Build `
    -logFile "$env:TEMP\build_linux.log"
```

Note that Unity keeps running after that command appears to return, so wait for the
`Unity` process to disappear and check the log for `[BuildLinuxHeadless] SUCCESS`
rather than trusting the exit code.

## 1. Rent the instance (you have to do this part)

Jarvis Labs -> Templates -> PyTorch:

| Setting | Value | Why |
|---|---|---|
| GPU | **L4** (IN2) | 28 vCPU / 124GB RAM for Rs 41.31/hr. The A30 is cheaper (Rs 38.88) but has only 16 vCPU, and this workload is CPU-bound, so L4 is ~75% more capacity for ~3.5% more money. |
| Quantity | 1 GPU | The GPU is barely used at all (see below). |
| Pricing | On-Demand | Spot is 33% cheaper but interruptible. Spot is fine *for the smoke test*, since losing it costs nothing. |
| **Storage** | **100 GB** | The default (20GB) is too small. Costs ~Rs 1.30/hr. |

All-in: about **Rs 42.61/hr**.

> The GPU is nearly irrelevant here. HB_01 spent 95.5% of its wall clock inside Unity
> physics (CPU) and only 3.5% on gradients (GPU). We rent this box for its 28 vCPUs.

## 2. Upload

Do **not** upload `Training\` wholesale. It contains `config\results\HB_01\` -- 31 MB
of the old run, which is useless on the server (the network input changed from 10 to
30 with the stacked-vector fix, so nothing can warm-start from it) and is also the
only copy of the 41.29 baseline you are trying to beat. Leave it safely at home.

From Windows PowerShell, in `...\Mini Project\`. Replace host/port with what Jarvis
Labs shows you:

```powershell
$H = "<HOST>"; $P = "<PORT>"          # set these once

ssh -p $P root@$H "mkdir -p ~/Training/config"
scp -P $P -r .\Training\build                       root@${H}:~/Training/
scp -P $P    .\Training\train_16.sh                 root@${H}:~/Training/
scp -P $P    .\Training\config\trainer_config_16.yaml root@${H}:~/Training/config/
```

That is ~156 MB, almost all of it the build. On a slow connection this is the longest
step -- start it before you need the box, since **billing starts when the instance
does, not when you start training**.

## 3. Prepare the box (on the server, over SSH)

```bash
# The executable bit does not survive an upload from Windows. Without this the run
# dies with a confusing "permission denied". train_16.sh also fixes it automatically.
chmod +x ~/Training/build/Hummingbird.x86_64

pip install mlagents==1.1.0

# tmux keeps the run alive if your SSH connection drops. Skipping this means a
# dropped connection kills a run you are paying for.
tmux new -s hb
cd ~/Training
```

## 4. Smoke test -- does it run at all?

```bash
ISLANDS=28 ./train_16.sh smoke
```

Let it run ~10 minutes. In a second terminal (`tmux new-window`, or a second SSH):

```bash
./train_16.sh status      # steps/sec
htop                      # are all 28 vCPUs busy, or is one pegged at 100%?
grep IslandSpawner results/HB_16_smoke_unity.log    # confirm the count took effect
```

You are checking three things: it starts, the mean reward is climbing off zero, and
the island count actually took effect. Then Ctrl+C.

## 5. Sweep -- what is the FASTEST setup? (the real experiment)

Do not hand-guess this. Run:

```bash
./train_16.sh sweep
```

It runs six ways of splitting **the same 28 arenas** -- 28x1, 14x2, 7x4, 4x7, 2x14,
1x28 -- for 4 minutes each, then prints a ranked table and the exact command for the
winner. About 25 minutes, ~Rs 18 of rental, and it replaces a lot of guesswork.

**Why the split matters so much:** Unity's main loop -- and therefore every agent's
observation and action code -- runs on **one thread per process**. So 28 islands in
one process step one after another on a single core, potentially leaving 27 of the
L4's vCPUs idle. The same 28 arenas as 28 separate processes uses 28 cores. But more
processes also multiply memory and scene-loading time, so the best answer is usually
somewhere in the middle and cannot be predicted -- only measured.

Then repeat on CPU and compare the two winners:

```bash
DEVICE=cpu ./train_16.sh sweep
```

The GPU is a real candidate to lose here: HB_01 spent only 3.5% of its wall clock on
gradients, and the network is a small 30 -> 256 -> 256 -> 5 MLP where per-operation
GPU launch overhead can outweigh the speedup.

If the fastest combo is at the edge of the list (28x1 or 1x28), the true optimum may
lie beyond it -- try a wider set:

```bash
COMBOS="56:1 8:7 2:28 1:56" ./train_16.sh sweep
```

## 6. The real run

1. Put the winning steps/sec into `config/trainer_config_16.yaml` and set `max_steps`
   to what your budget covers. Do not change it mid-run: the learning rate anneals
   linearly to ~0 exactly at `max_steps`, so stopping early leaves the policy
   under-trained and changing it later invalidates the schedule.
2. Delete the smoke results so the real run starts clean: `rm -rf results/`
3. Start it, inside tmux, with the winning settings:

```bash
ISLANDS=<winner> DEVICE=<winner> ./train_16.sh
```

Checkpoints save every 250,000 steps (~9 min at 450 steps/s), 40 kept, so an
interruption costs minutes rather than the whole run. Re-running the same command
auto-resumes.

## 7. Getting the result back

```powershell
scp -P <PORT> -r root@<HOST>:~/Training/results ./results_from_server
```

The trained model is `results/HB_16/Hummingbird.onnx`. Drop it into
`Assets/Hummingbird/NN Models/` in the Unity project to watch it fly.

**Destroy the instance when done** -- a paused instance still bills for storage.

## The number to beat

HB_01 finished at mean reward **41.29**. `./train_16.sh status` prints the comparison
automatically. Two things are expected to beat it: far more steps (HB_01 was still
climbing when it hit its budget, it never plateaued), and the Stacked Vectors 1 -> 3
change that finally lets the bird perceive its own momentum.

Do **not** chase a higher number by editing the reward function, `MaxStep`, or the
bird's physics. All three scale episode reward directly, so they inflate the score
without the bird flying any better and destroy comparability with 41.29.
