// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_segments_tld_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop
{
    // Segments thread local data
    internal unsafe struct mi_segments_tld_t
    {
        // queue of segments with free small pages
        public mi_segment_queue_t small_free;

        // queue of segments with free medium pages
        public mi_segment_queue_t medium_free;

        // queue of freed pages that can be reset
        public mi_page_queue_t pages_reset;

        // current number of segments;
        [NativeTypeName("nuint")]
        public nuint count;

        // peak number of segments
        [NativeTypeName("nuint")]
        public nuint peak_count;

        // current size of all segments
        [NativeTypeName("nuint")]
        public nuint current_size;

        // peak size of all segments
        [NativeTypeName("nuint")]
        public nuint peak_size;

        // number of segments in the cache
        [NativeTypeName("nuint")]
        public nuint cache_count;

        // total size of all segments in the cache
        [NativeTypeName("nuint")]
        public nuint cache_size;

        // (small) cache of segments
        public mi_segment_t* cache;

        // points to tld stats
        public mi_stats_t* stats;

        // points to os stats
        public mi_os_tld_t* os;
    }
}
