// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_tld_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop
{
    // Thread local data
    internal unsafe struct mi_tld_t
    {
        // monotonic heartbeat count
        [NativeTypeName("unsigned long long")]
        public ulong heartbeat;

        // true if deferred was called; used to prevent infinite recursion.
        public bool recurse;

        // backing heap of this thread (cannot be deleted)
        public mi_heap_t* heap_backing;

        // list of heaps in this thread (so we can abandon all when the thread terminates)
        public mi_heap_t* heaps;

        // segment tld
        public mi_segments_tld_t segments;

        // os tld
        public mi_os_tld_t os;

        // statistics
        public mi_stats_t stats;
    }
}
