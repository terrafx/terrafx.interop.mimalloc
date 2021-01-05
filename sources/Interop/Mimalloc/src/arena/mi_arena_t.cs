// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_arena_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop
{
    // A memory arena descriptor
    internal unsafe struct mi_arena_t
    {
        // the start of the memory area
        [NativeTypeName("std::atomic<uint8_t*>")]
        public volatile nuint start;

        // size of the area in arena blocks (of `MI_ARENA_BLOCK_SIZE`)
        [NativeTypeName("size_t")]
        public nuint block_count;

        // number of bitmap fields (where `field_count * MI_BITMAP_FIELD_BITS >= block_count`)
        [NativeTypeName("size_t")]
        public nuint field_count;

        // associated NUMA node
        public int numa_node;

        // is the arena zero initialized?
        public bool is_zero_init;

        // is the memory committed
        public bool is_committed;

        // large OS page allocated
        public bool is_large;

        // optimization to start the search for free blocks
        [NativeTypeName("std::atomic<uintptr_t>")]
        public volatile nuint search_idx;

        // are the blocks potentially non-zero?
        [NativeTypeName("mi_bitmap_field_t*")]
        public volatile nuint* blocks_dirty;

        // if `!is_committed`, are the blocks committed?
        [NativeTypeName("mi_bitmap_field_t*")]
        public volatile nuint* blocks_committed;

        // in-place bitmap of in-use blocks (of size `field_count`)
        [NativeTypeName("mi_bitmap_field_t [1]")]
        public _blocks_inuse_e__FixedBuffer blocks_inuse;

        public struct _blocks_inuse_e__FixedBuffer
        {
            public volatile nuint e0;
        }
    }
}
