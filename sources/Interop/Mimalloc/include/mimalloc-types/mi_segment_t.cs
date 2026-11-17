// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_segment_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

// Segments are large allocated memory blocks (2mb on 64 bit) from
// the OS. Inside segments we allocated fixed size _pages_ that
// contain blocks.
internal unsafe struct mi_segment_t
{
    // memory fields

    // id for the os-level memory manager
    [NativeTypeName("size_t")]
    public mi_memid_t memid;

    [NativeTypeName("size_t")]
    public nuint mem_alignment;

    [NativeTypeName("size_t")]
    public nuint mem_align_offset;

    public bool allow_decommit;

    public bool allow_purge;

    // `true` if we cannot decommit/reset/protect in this memory
    public bool mem_is_pinned;

    // `true` if the memory is in OS large or huge pages
    public bool mem_is_large;

    // `true` if the whole segment is eagerly committed
    public bool mem_is_committed;

    // segment fields

    // must be the first segment field after the page metadata
    public mi_segment_t* next;

    public mi_segment_t* prev;

    // true if the segment was reclaimed from another thread
    public bool was_reclaimed;

    // temporarily prevents freeing the segment during forced abandonment
    public bool dont_free;

    // abandoned pages (i.e. the original owning thread stopped) (`abandoned <= used`)
    [NativeTypeName("size_t")]
    public nuint abandoned;

    // count how often this segment is visited in the abandoned list (to force reclaim it it is too long)
    [NativeTypeName("size_t")]
    public nuint abandoned_visits;

    // count of pages in use (`used <= capacity`)
    [NativeTypeName("size_t")]
    public nuint used;

    // count of available pages (`#free + used`)
    [NativeTypeName("size_t")]
    public nuint capacity;

    // for huge pages this may be different from `MI_SEGMENT_SIZE`
    [NativeTypeName("size_t")]
    public nuint segment_size;

    public mi_subproc_t* subproc;

    // space we are using from the first page for segment meta-data and possible guard pages.
    [NativeTypeName("size_t")]
    public nuint segment_info_size;

    // verify addresses in secure mode: `_mi_ptr_cookie(segment) == segment->cookie`
    [NativeTypeName("uintptr_t")]
    public nuint cookie;

    // links for OS-allocated abandoned segments
    public mi_segment_t* abandoned_os_next;

    public mi_segment_t* abandoned_os_prev;

    // layout like this to optimize access in `mi_free`

    // unique id of the thread owning this segment
    [NativeTypeName("std::atomic<uintptr_t>")]
    public volatile nuint thread_id;

    // `1 << page_shift` == the page sizes == `page->block_size * page->reserved` (unless the first page, then `-segment_info_size`).
    [NativeTypeName("size_t")]
    public nuint page_shift;

    // kind of pages: small, large, or huge
    public mi_page_kind_t page_kind;

    // up to `MI_SMALL_PAGES_PER_SEGMENT` pages
    [NativeTypeName("mi_page_t [1]")]
    public _pages_e__FixedBuffer pages;

    public partial struct _pages_e__FixedBuffer
    {
        public mi_page_t e0;
    }
}
