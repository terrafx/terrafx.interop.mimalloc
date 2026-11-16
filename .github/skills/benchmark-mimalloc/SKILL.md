---
name: benchmark-mimalloc
description: Measure allocator performance or validate a performance claim with the existing BenchmarkDotNet harness. Use for allocator optimizations and benchmark investigations; not as a mandatory step for unrelated changes.
---

# Measuring allocator performance

Follow [AGENTS.md](../../../AGENTS.md) for build and correctness validation.
All paths below are relative to the repository root.

## Establish the comparison

Capture a baseline before changing the implementation. Record the revision or
preserved patch, SDK/runtime, OS, process architecture, build configuration,
and relevant allocator settings. Keep the workload, toolchain, hardware, and
settings identical between baseline and candidate runs.

Read `benchmarks\Interop\Mimalloc\Bench.cs` and `Program.cs` before selecting
cases. The existing harness compares mimalloc, `new byte[]`,
`NativeMemory.Alloc`, and `Marshal.AllocHGlobal` across `Size` and `IterCount`.
Its unmanaged cases allocate and free; the managed array case allocates a
zero-initialized array without an explicit free. These are not identical
zeroing or reclamation contracts.

The reported method timing includes `IterCount` operations. Do not present
different iteration counts as per-allocation latency without normalization.
The harness is single-threaded and does not establish cross-thread-free
throughput, fragmentation, or peak-memory behavior.

## Run the existing harness

Restore dependencies using the normal commands in `AGENTS.md` when needed.
From the repository root, run:

```powershell
dotnet run --project .\benchmarks\Interop\Mimalloc\TerraFX.Interop.Mimalloc.Benchmarks.csproj -c Release --no-restore
```

`Program.cs` calls `BenchmarkRunner.Run<AllocatorBenchmarks>()` without
forwarding command-line arguments. Flags after `--`, such as `--filter` or
`--job`, do not select cases in this harness. Confirm that the expected methods
and parameter combinations actually completed; do not treat an unexecuted or
partially failed run as evidence.

Use Release builds without a debugger. The allocator's Debug diagnostics and
checked arithmetic differ from Release. Set AppContext-backed settings before
the first allocator access, including in BenchmarkDotNet child processes if a
task requires non-default settings.

BenchmarkDotNet writes reports and logs to `BenchmarkDotNet.Artifacts` by
default. Preserve baseline and candidate reports separately under ignored
`artifacts` before another run overwrites them. Do not commit generated
benchmark output.

## Interpret the result

Compare the same method and parameter values across revisions. Use the
reported distributions and uncertainty; repeat measurements when a proposed
improvement is comparable to noise. Check the relevant individual workloads,
not only an aggregate that can hide regressions.

For a workload the existing matrix cannot represent, add only the benchmark
needed for the requested contract, following the existing harness. Keep
allocation/free pairs valid and prevent dead-code elimination. Do not change
the workload between baseline and candidate or bundle unrelated harness work.

Report the measured effect, any regressions, and limitations. Historical
reports under `docs\benchmarks` are context, not a current-machine baseline.
Correctness tests remain separate; faster timings do not establish memory
safety or allocator contract preservation.
