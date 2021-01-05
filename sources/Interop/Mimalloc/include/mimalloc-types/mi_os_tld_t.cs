// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_os_tld_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop
{
    // OS thread local data
    internal unsafe struct mi_os_tld_t
    {
        // start point for next allocation
        [NativeTypeName("size_t")]
        public nuint region_idx;

        // points to tld stats
        public mi_stats_t* stats;
    }
}
