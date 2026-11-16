# Repository guidance

TerraFX.Interop.Mimalloc is a managed C# port of
[microsoft/mimalloc](https://github.com/microsoft/mimalloc), not a P/Invoke
wrapper around a native mimalloc binary. The allocator implementation is
maintained here; there is no binding-regeneration step.

## Repository map

| Path | Purpose |
| --- | --- |
| `sources\Interop\Mimalloc\include` | Ported public declarations, allocator types, internal helpers, and atomic operations |
| `sources\Interop\Mimalloc\src` | Ported allocator implementation, grouped by the corresponding upstream source file |
| `sources\Interop\Mimalloc\Mimalloc*.cs` | .NET initialization, runtime helpers, libc calls, and Windows/Unix interop |
| `sources\Interop\Mimalloc\Configuration.cs`, `ILLink.Substitutions.xml` | Native-library resolver feature switch and linker substitutions |
| `tests\Interop\Mimalloc\test\main` | NUnit allocator tests |
| `benchmarks\Interop\Mimalloc` | BenchmarkDotNet allocator comparisons |
| `scripts\build.ps1`, `scripts\build.sh` | Restore, build, test, and pack entry points |
| `Directory.Build.props`, `Directory.Build.targets` | Shared analyzer, signing, trimming, and artifact settings |
| `Directory.Packages.props` | Central NuGet package versions |
| `.github\workflows\ci.yml` | Cross-platform Debug/Release validation and packaging |
| `docs` | Project goals, contributor guidance, and benchmark reports |

Read `docs\README.md` and `docs\CONTRIBUTING.md` for contribution policy.
Follow `.editorconfig` and the interop-specific overrides under `sources` and
`tests`. Preserve native `mi_*` naming, `NativeTypeName` annotations, layout,
and upstream attribution. New C# files need the existing license header.

## Allocator changes

Public partial declarations under `include\mimalloc` and implementations under
`src` are maintained together. Match the native revision relevant to the
requested change, not an arbitrary current upstream implementation. Use the
[upstream-porting skill](.github/skills/port-upstream-mimalloc/SKILL.md) when
porting or comparing an upstream change.

Preserve pointer-width arithmetic, overflow checks, alignment, zeroing,
reallocation ownership, and null/error behavior. Keep native metadata free of
managed references and establish pointer lifetimes explicitly. Reuse existing
size, alignment, page, and segment helpers rather than duplicating their logic.

Heap allocation is thread-affine; cross-thread frees follow a separate delayed
free path. Changes to heaps, pages, segments, atomics, or thread cleanup must
preserve their ownership and synchronization invariants. Check the actual
`Interlocked`/`Volatile` implementation of an atomic helper, including its
return value, rather than assuming its name establishes its semantics.

`MI_DEBUG`, `MI_SECURE`, `MI_PADDING`, and `MI_STAT` are read from AppContext
during static initialization in `Mimalloc.Init.cs`. Set configuration before
the first allocator access; use separate processes when testing different
initialization settings. Preserve trimming and Native AOT compatibility,
including resolver behavior and `ILLink.Substitutions.xml`.

The existing `MimallocTests.MainTest` is a smoke test, not comprehensive
allocator coverage. Add focused NUnit regression cases under the matching test
area for changed behavior. Cover the relevant size/alignment boundaries,
preserved or zeroed bytes, and allocation/free ownership; do not rely only on
the absence of a crash. Isolate tests that intentionally terminate the process.

Avoid adding managed allocations, locks, or extra work to allocator hot paths
without evidence. Use the
[benchmarking skill](.github/skills/benchmark-mimalloc/SKILL.md) for performance
investigations and claims, not as a requirement for unrelated changes.

## Build and validation

Run commands from the repository root. `global.json` pins the .NET 11 SDK,
and all three projects target `net11.0` first and `net10.0` second. The .NET 11
SDK builds both targets; tests for `net10.0` also require a .NET 10 runtime.
The architecture-specific build-script setup installs the .NET 11 SDK and
.NET 10 runtime side by side. No native mimalloc library, ClangSharp
installation, or native SDK headers are needed to build this managed port.

PowerShell examples:

| Intent | Command |
| --- | --- |
| Restore and build Debug | `pwsh -NoLogo -NoProfile -File .\scripts\build.ps1 -restore -build` |
| Build and test Debug | `pwsh -NoLogo -NoProfile -File .\scripts\build.ps1 -restore -build -test` |
| Build and test Release | `pwsh -NoLogo -NoProfile -File .\scripts\build.ps1 -configuration Release -restore -build -test` |
| Focused test example | `dotnet test .\tests\Interop\Mimalloc\TerraFX.Interop.Mimalloc.UnitTests.csproj -c Debug --filter "FullyQualifiedName~MimallocTests.MainTest"` |
| Pack an existing Release build | `pwsh -NoLogo -NoProfile -File .\scripts\build.ps1 -configuration Release -pack` |

On Linux/macOS, `bash ./scripts/build.sh --restore --build --test` performs
the corresponding actions; use `--configuration Release` for Release.

The root `build`, `restore`, `test`, and `pack` wrappers select individual
actions. The central scripts' build action skips restore; test and pack skip
both build and restore. Select preceding actions together when outputs are
missing or stale. `-solution` / `--solution` can select an individual project.
Reuse current restore/build outputs when appropriate.

Validate a coherent batch with the smallest relevant build and test selection.
Debug enables checked arithmetic and different allocator diagnostics from
Release, so allocator arithmetic, initialization, and hot-path changes need
both configurations. Confirm that filtered tests actually ran.

CI covers Windows and Linux x64/arm64 and macOS arm64 in both configurations
and targets both TFMs.
OS memory allocation, page sizes, libc entry points, and optional APIs differ;
success on one host does not establish other platform or architecture coverage.
Passing `-architecture` / `--architecture` downloads a local SDK for that
invocation; it is not merely a target-platform property.

Outputs are under `artifacts\bin`, `artifacts\obj`, `artifacts\pkg`, and
`artifacts\tst`. Scripted build logs are under
`artifacts\log\<Configuration>`. Keep run-specific evidence in ignored
artifacts rather than maintained documentation.

For instruction- or documentation-only changes, check metadata, links, paths,
and command consistency; no product build is needed. Changes to executable
setup or workflow commands also need the relevant command/configuration checks.
Report unavailable checks and platform coverage explicitly.
