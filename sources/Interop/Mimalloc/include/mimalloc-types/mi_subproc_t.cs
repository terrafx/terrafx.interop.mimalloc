// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_subproc_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal unsafe struct mi_subproc_t
{
    public nuint abandoned_count;

    public nuint abandoned_os_list_count;

    public int abandoned_os_lock;

    public int abandoned_os_visit_lock;

    public mi_segment_t* abandoned_os_list;

    public mi_segment_t* abandoned_os_list_tail;

    public mi_memid_t memid;
}
