// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mem_region_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // A region owns a chunk of REGION_SIZE (256MiB) (virtual) memory with
    // a bit map with one bit per MI_SEGMENT_SIZE (4MiB) block.
    internal unsafe struct mem_region_t
    {
        // mi_region_info_t.value
        [NativeTypeName("std::atomic<uintptr_t>")]
        public volatile nuint info;

        // start of the memory area 
        [NativeTypeName("std::atomic<void*>")]
        public volatile nuint start;

        // bit per in-use block
        [NativeTypeName("mi_bitmap_field_t")]
        public volatile nuint in_use;

        // track if non-zero per block
        [NativeTypeName("mi_bitmap_field_t")]
        public volatile nuint dirty;

        // track if committed per block
        [NativeTypeName("mi_bitmap_field_t")]
        public volatile nuint commit;

        // track if reset per block
        [NativeTypeName("mi_bitmap_field_t")]
        public volatile nuint reset;

        // if allocated from a (huge page) arena
        [NativeTypeName("std::atomic<uintptr_t>")]
        public volatile nuint arena_memid;

        // round to 8 fields
        [NativeTypeName("uintptr_t")]
        public volatile nuint padding;
    }
}
