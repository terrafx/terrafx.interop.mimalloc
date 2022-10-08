// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_page_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

// A page contains blocks of one specific size (`block_size`).
//
// Each page has three list of free blocks:
// * `free` for blocks that can be allocated,
// * `local_free` for freed blocks that are not yet available to `mi_malloc`
// * `thread_free` for freed blocks by other threads
//
// The `local_free` and `thread_free` lists are migrated to the `free` list
// when it is exhausted. The separate `local_free` list is necessary to
// implement a monotonic heartbeat. The `thread_free` list is needed for
// avoiding atomic operations in the common case.
//
// `used - |thread_free|` == actual blocks that are in use (alive)
// `used - |thread_free| + |free| + |local_free| == capacity`
//
// We don't count `freed` (as |free|) but use `used` to reduce
// the number of memory accesses in the `mi_page_all_free` function(s).
//
// Notes: 
// - Access is optimized for `mi_free` and `mi_page_alloc` (in `alloc.c`)
// - Using `ushort` does not seem to slow things down
// - The size is 8 words on 64-bit which helps the page index calculations
//   (and 10 words on 32-bit, and encoded free lists add 2 words. Sizes 10 
//    and 12 are still good for address calculation)
// - To limit the structure size, the `xblock_size` is 32-bits only; for 
//   blocks > MI_HUGE_BLOCK_SIZE the size is determined from the segment page size
// - `thread_free` uses the bottom bits as a delayed-free flags to optimize
//   concurrent frees where only the first concurrent free adds to the owning
//   heap `thread_delayed_free` list (see `alloc.c:mi_free_block_mt`).
//   The invariant is that no-delayed-free is only set if there is
//   at least one block that will be added, or as already been added, to 
//   the owning heap `thread_delayed_free` list. This guarantees that pages
//   will be freed correctly even if only other threads free blocks.
internal unsafe struct mi_page_t
{
    // "owned" by the segment

    // index in the segment `pages` array, `page == (&segment->pages.e0 + page->segment_idx)`
    [NativeTypeName("uint8_t")]
    public byte segment_idx;

    public byte _bitfield1;

    // `true` if the segment allocated this page
    [NativeTypeName("uint8_t : 1")]
    public bool segment_in_use
    {
        get
        {
            return (_bitfield1 & 0x1u) != 0;
        }

        set
        {
            _bitfield1 = (byte)((_bitfield1 & ~0x1u) | (value ? 1u : 0u));
        }
    }

    // `true` if the page memory was reset
    [NativeTypeName("uint8_t : 1")]
    public bool is_reset
    {
        get
        {
            return ((_bitfield1 >> 1) & 0x1u) != 0;
        }

        set
        {
            _bitfield1 = (byte)((_bitfield1 & ~(0x1u << 1)) | ((value ? 1u : 0u) << 1));
        }
    }

    // `true` if the page virtual memory is committed
    [NativeTypeName("uint8_t : 1")]
    public bool is_committed
    {
        get
        {
            return ((_bitfield1 >> 2) & 0x1u) != 0;
        }

        set
        {
            _bitfield1 = (byte)((_bitfield1 & ~(0x1u << 2)) | ((value ? 1u : 0u) << 2));
        }
    }

    // `true` if the page was zero initialized
    [NativeTypeName("uint8_t : 1")]
    public bool is_zero_init
    {
        get
        {
            return ((_bitfield1 >> 3) & 0x1u) != 0;
        }

        set
        {
            _bitfield1 = (byte)((_bitfield1 & ~(0x1u << 3)) | ((value ? 1u : 0u) << 3));
        }
    }

    // layout like this to optimize access in `mi_malloc` and `mi_free`

    // number of blocks committed, must be the first field, see `segment.c:page_clear`
    [NativeTypeName("uint16_t")]
    public ushort capacity;

    // number of blocks reserved in memory
    [NativeTypeName("uint16_t")]
    public ushort reserved;

    // `in_full` and `has_aligned` flags (8 bits)
    public mi_page_flags_t flags;

    private byte _bitfield2;

    // `true` if the blocks in the free list are zero initialized
    [NativeTypeName("uint8_t : 1")]
    public bool is_zero
    {
        get
        {
            return (_bitfield2 & 0x1u) != 0;
        }

        set
        {
            _bitfield2 = (byte)((_bitfield2 & ~0x1u) | (value ? 1u : 0u));
        }
    }

    // expiration count for retired blocks
    [NativeTypeName("uint8_t : 7")]
    public byte retire_expire
    {
        get
        {
            return (byte)((_bitfield2 >> 1) & 0x7Fu);
        }

        set
        {
            _bitfield2 = (byte)((_bitfield2 & ~(0x7Fu << 1)) | ((value & 0x7Fu) << 1));
        }
    }

    // list of available free blocks (`malloc` allocates from this list)
    public mi_block_t* free;

    // two random keys to encode the free lists (see `_mi_block_next`)
    [NativeTypeName("uintptr_t [2]")]
    public _keys_e__FixedBuffer keys;

    // number of blocks in use (including blocks in `local_free` and `thread_free`)
    [NativeTypeName("uint32_t")]
    public uint used;

    // size available in each block (always `>0`)
    [NativeTypeName("uint32_t")]
    public uint xblock_size;

    // list of deferred free blocks by this thread (migrates to `free`)
    public mi_block_t* local_free;

    // list of deferred free blocks freed by other threads
    [NativeTypeName("std::atomic<mi_thread_free_t>")]
    public volatile nuint xthread_free;

    [NativeTypeName("std::atomic<nuint>")]
    public volatile nuint xheap;

    // next page owned by this thread with the same `block_size`
    public mi_page_t* next;

    // previous page owned by this thread with the same `block_size`
    public mi_page_t* prev;

    public partial struct _keys_e__FixedBuffer
    {
        public nuint e0;
        public nuint e1;
    }
}
