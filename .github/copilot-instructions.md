# TerraFX.Interop.Mimalloc

Follow [AGENTS.md](../AGENTS.md) for repository layout, allocator invariants,
editing conventions, and build/test commands.

This is a managed port of mimalloc, not generated bindings or a wrapper over
a native mimalloc library. Preserve native naming and semantics, unsafe memory
ownership, cross-platform behavior, trimming safety, and Native AOT compatibility.

Use the [upstream-porting skill](skills/port-upstream-mimalloc/SKILL.md) for
upstream fixes and the [benchmarking skill](skills/benchmark-mimalloc/SKILL.md)
when measuring allocator performance. The
[mimalloc maintainer agent](agents/mimalloc-maintainer.agent.md) specializes in
allocator implementation and its regression coverage.
