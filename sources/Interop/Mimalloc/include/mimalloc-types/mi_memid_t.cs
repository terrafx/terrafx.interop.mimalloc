// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_memid_t type from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.InteropServices;

namespace TerraFX.Interop.Mimalloc;

internal enum mi_memkind_t
{
    MI_MEM_NONE,
    MI_MEM_EXTERNAL,
    MI_MEM_STATIC,
    MI_MEM_OS,
    MI_MEM_OS_HUGE,
    MI_MEM_OS_REMAP,
    MI_MEM_ARENA,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct mi_memid_os_info_t
{
    public void* @base;

    [NativeTypeName("size_t")]
    public nuint size;
}

[StructLayout(LayoutKind.Sequential)]
internal struct mi_memid_arena_info_t
{
    [NativeTypeName("size_t")]
    public nuint block_index;

    [NativeTypeName("mi_arena_id_t")]
    public int id;

    public bool is_exclusive;
}

[StructLayout(LayoutKind.Explicit)]
internal struct mi_memid_union_t
{
    [FieldOffset(0)]
    public mi_memid_os_info_t os;

    [FieldOffset(0)]
    public mi_memid_arena_info_t arena;
}

[StructLayout(LayoutKind.Sequential)]
internal struct mi_memid_t
{
    public mi_memid_union_t mem;

    public bool is_pinned;

    public bool is_large;

    public bool initially_committed;

    public bool initially_zero;

    public mi_memkind_t memkind;
}
