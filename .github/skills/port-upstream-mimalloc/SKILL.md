---
name: port-upstream-mimalloc
description: Port or compare a microsoft/mimalloc fix in the managed C# allocator. Use for upstream algorithm, API, metadata, atomic, or OS-memory changes; not for binding regeneration or routine build work.
---

# Porting an upstream mimalloc change

Follow [AGENTS.md](../../../AGENTS.md) for normal repository interaction.
All paths below are relative to the repository root.

## Establish the native baseline

Identify the upstream issue, commit, or tag and the behavior it changes.
Read `MI_MALLOC_VERSION` in
`sources\Interop\Mimalloc\include\mimalloc\Mimalloc.cs`, the affected files'
native-source attribution, and relevant Git history. Do not equate the NuGet
package version with an exact upstream source revision or apply current
upstream code to an older algorithm without checking its prerequisites.

Compare the affected native implementation before and after the change,
including its types, macros, and tests. Establish which prerequisites are
already present in this port and which would change the requested scope.
Retain uncertainty about the source baseline if it cannot be verified.

## Find the corresponding managed layers

| Native area | Managed area under `sources\Interop\Mimalloc` |
| --- | --- |
| `include/mimalloc.h` | `include\mimalloc`: public partial declarations and callback types |
| Internal/type headers | `include\mimalloc-internal`, `include\mimalloc-types` |
| Atomic helpers | `include\mimalloc-atomic` |
| `src/alloc*.c`, heap/page/segment algorithms | Corresponding directories under `src` |
| OS allocation and runtime initialization | `src\os`, `src\init`, and root `Mimalloc*.cs` helpers |

Search for existing equivalents before adding declarations or helpers. Changes
to a public partial method must keep declarations and implementations aligned.
These are maintained C# sources, not files to regenerate with ClangSharp.
Preserve their license and native-source attribution.

## Preserve the contract across languages

Check the affected invariants rather than transliterating the native diff:

- Native-width signed/unsigned arithmetic, multiplication overflow, shifts,
  alignment masks, sentinel values, and intentionally wrapping operations.
  Debug uses checked arithmetic; use explicit `unchecked` only where the
  algorithm requires wrapping.
- Metadata layout, bit encodings, free-list links, padding, and the lifetime
  of every pointer retained in unmanaged storage.
- Heap thread ownership, cross-thread delayed frees, abandoned-page/segment
  reclamation, and initialization/teardown ordering.
- Atomic old/new return values, compare-exchange expected-value updates, and
  ordering. Verify the C# helper implementation, not just its memory-order
  argument or name.
- Allocation size/zeroing, reallocation data preservation and failure
  ownership, alignment/offset behavior, and error reporting for the specific API.
- Windows, Linux, and macOS allocation/protection behavior, native type widths,
  and optional entry points. Reuse the port's existing platform abstractions.

AppContext-backed allocator settings are captured during static initialization.
Set them before first use and isolate configuration variants in separate
processes. Do not alter unrelated defaults, public APIs, package versions, or
resolver/linker settings as an incidental part of the port.

## Regress and validate

Add focused NUnit cases under `tests\Interop\Mimalloc` that fail without the
change. Follow `test\main\MimallocTests.cs` and nearby tests, using data-driven
cases for relevant boundaries rather than duplicating the smoke test.

Assert observable contracts such as returned alignment, zeroed/preserved bytes,
usable size, and ownership after failure. Free successful allocations with the
matching allocator and keep heap allocation on its owning thread. For a
threading fix, coordinate the relevant interleaving instead of relying only on
unbounded stress. Process-terminating cases need process isolation.

Use the build/test commands in `AGENTS.md`, covering Debug and Release for
allocator behavior. Confirm selected tests ran and account for affected
platforms/architectures that could not be exercised. Benchmark only when the
change affects a performance contract or makes a performance claim, using the
[benchmarking skill](../benchmark-mimalloc/SKILL.md).
