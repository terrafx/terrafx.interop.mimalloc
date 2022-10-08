// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_heap_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

// A heap owns a set of pages.
internal unsafe struct mi_heap_t
{
    public mi_tld_t* tld;

    // optimize: array where every entry points a page with possibly free blocks in the corresponding queue for that size.
    [NativeTypeName("mi_page_t* [MI_PAGES_DIRECT]")]
    public _pages_free_direct_e__FixedBuffer pages_free_direct;

    // queue of pages for each size class (or "bin")
    [NativeTypeName("mi_page_queue_t [MI_BIN_FULL + 1]")]
    public _pages_e__FixedBuffer pages;

    [NativeTypeName("std::atomic<mi_block_t*>")]
    public volatile nuint thread_delayed_free;

    // thread this heap belongs too
    [NativeTypeName("uintptr_t")]
    public nuint thread_id;

    // random cookie to verify pointers (see `_mi_ptr_cookie`)
    [NativeTypeName("uintptr_t")]
    public nuint cookie;

    // two random keys used to encode the `thread_delayed_free` list
    [NativeTypeName("uintptr_t [2]")]
    public _keys_e__FixedBuffer keys;

    // random number context used for secure allocation
    public mi_random_ctx_t random;

    // total number of pages in the `pages` queues.
    [NativeTypeName("size_t")]
    public nuint page_count;

    // smallest retired index (retired pages are fully free, but still in the page queues)
    [NativeTypeName("size_t")]
    public nuint page_retired_min;

    // largest retired index into the `pages` array.
    [NativeTypeName("size_t")]
    public nuint page_retired_max;

    // list of heaps per thread
    public mi_heap_t* next;

    // `true` if this heap should not reclaim abandoned pages
    public bool no_reclaim;

    public partial struct _pages_free_direct_e__FixedBuffer
    {
        public mi_page_t* e0;
        public mi_page_t* e1;
        public mi_page_t* e2;
        public mi_page_t* e3;
        public mi_page_t* e4;
        public mi_page_t* e5;
        public mi_page_t* e6;
        public mi_page_t* e7;
        public mi_page_t* e8;
        public mi_page_t* e9;
        public mi_page_t* e10;
        public mi_page_t* e11;
        public mi_page_t* e12;
        public mi_page_t* e13;
        public mi_page_t* e14;
        public mi_page_t* e15;
        public mi_page_t* e16;
        public mi_page_t* e17;
        public mi_page_t* e18;
        public mi_page_t* e19;
        public mi_page_t* e20;
        public mi_page_t* e21;
        public mi_page_t* e22;
        public mi_page_t* e23;
        public mi_page_t* e24;
        public mi_page_t* e25;
        public mi_page_t* e26;
        public mi_page_t* e27;
        public mi_page_t* e28;
        public mi_page_t* e29;
        public mi_page_t* e30;
        public mi_page_t* e31;
        public mi_page_t* e32;
        public mi_page_t* e33;
        public mi_page_t* e34;
        public mi_page_t* e35;
        public mi_page_t* e36;
        public mi_page_t* e37;
        public mi_page_t* e38;
        public mi_page_t* e39;
        public mi_page_t* e40;
        public mi_page_t* e41;
        public mi_page_t* e42;
        public mi_page_t* e43;
        public mi_page_t* e44;
        public mi_page_t* e45;
        public mi_page_t* e46;
        public mi_page_t* e47;
        public mi_page_t* e48;
        public mi_page_t* e49;
        public mi_page_t* e50;
        public mi_page_t* e51;
        public mi_page_t* e52;
        public mi_page_t* e53;
        public mi_page_t* e54;
        public mi_page_t* e55;
        public mi_page_t* e56;
        public mi_page_t* e57;
        public mi_page_t* e58;
        public mi_page_t* e59;
        public mi_page_t* e60;
        public mi_page_t* e61;
        public mi_page_t* e62;
        public mi_page_t* e63;
        public mi_page_t* e64;
        public mi_page_t* e65;
        public mi_page_t* e66;
        public mi_page_t* e67;
        public mi_page_t* e68;
        public mi_page_t* e69;
        public mi_page_t* e70;
        public mi_page_t* e71;
        public mi_page_t* e72;
        public mi_page_t* e73;
        public mi_page_t* e74;
        public mi_page_t* e75;
        public mi_page_t* e76;
        public mi_page_t* e77;
        public mi_page_t* e78;
        public mi_page_t* e79;
        public mi_page_t* e80;
        public mi_page_t* e81;
        public mi_page_t* e82;
        public mi_page_t* e83;
        public mi_page_t* e84;
        public mi_page_t* e85;
        public mi_page_t* e86;
        public mi_page_t* e87;
        public mi_page_t* e88;
        public mi_page_t* e89;
        public mi_page_t* e90;
        public mi_page_t* e91;
        public mi_page_t* e92;
        public mi_page_t* e93;
        public mi_page_t* e94;
        public mi_page_t* e95;
        public mi_page_t* e96;
        public mi_page_t* e97;
        public mi_page_t* e98;
        public mi_page_t* e99;
        public mi_page_t* e100;
        public mi_page_t* e101;
        public mi_page_t* e102;
        public mi_page_t* e103;
        public mi_page_t* e104;
        public mi_page_t* e105;
        public mi_page_t* e106;
        public mi_page_t* e107;
        public mi_page_t* e108;
        public mi_page_t* e109;
        public mi_page_t* e110;
        public mi_page_t* e111;
        public mi_page_t* e112;
        public mi_page_t* e113;
        public mi_page_t* e114;
        public mi_page_t* e115;
        public mi_page_t* e116;
        public mi_page_t* e117;
        public mi_page_t* e118;
        public mi_page_t* e119;
        public mi_page_t* e120;
        public mi_page_t* e121;
        public mi_page_t* e122;
        public mi_page_t* e123;
        public mi_page_t* e124;
        public mi_page_t* e125;
        public mi_page_t* e126;
        public mi_page_t* e127;
        public mi_page_t* e128;
        public mi_page_t* e129;
        public mi_page_t* e130;
    }

    public partial struct _pages_e__FixedBuffer
    {
        public mi_page_queue_t e0;
        public mi_page_queue_t e1;
        public mi_page_queue_t e2;
        public mi_page_queue_t e3;
        public mi_page_queue_t e4;
        public mi_page_queue_t e5;
        public mi_page_queue_t e6;
        public mi_page_queue_t e7;
        public mi_page_queue_t e8;
        public mi_page_queue_t e9;
        public mi_page_queue_t e10;
        public mi_page_queue_t e11;
        public mi_page_queue_t e12;
        public mi_page_queue_t e13;
        public mi_page_queue_t e14;
        public mi_page_queue_t e15;
        public mi_page_queue_t e16;
        public mi_page_queue_t e17;
        public mi_page_queue_t e18;
        public mi_page_queue_t e19;
        public mi_page_queue_t e20;
        public mi_page_queue_t e21;
        public mi_page_queue_t e22;
        public mi_page_queue_t e23;
        public mi_page_queue_t e24;
        public mi_page_queue_t e25;
        public mi_page_queue_t e26;
        public mi_page_queue_t e27;
        public mi_page_queue_t e28;
        public mi_page_queue_t e29;
        public mi_page_queue_t e30;
        public mi_page_queue_t e31;
        public mi_page_queue_t e32;
        public mi_page_queue_t e33;
        public mi_page_queue_t e34;
        public mi_page_queue_t e35;
        public mi_page_queue_t e36;
        public mi_page_queue_t e37;
        public mi_page_queue_t e38;
        public mi_page_queue_t e39;
        public mi_page_queue_t e40;
        public mi_page_queue_t e41;
        public mi_page_queue_t e42;
        public mi_page_queue_t e43;
        public mi_page_queue_t e44;
        public mi_page_queue_t e45;
        public mi_page_queue_t e46;
        public mi_page_queue_t e47;
        public mi_page_queue_t e48;
        public mi_page_queue_t e49;
        public mi_page_queue_t e50;
        public mi_page_queue_t e51;
        public mi_page_queue_t e52;
        public mi_page_queue_t e53;
        public mi_page_queue_t e54;
        public mi_page_queue_t e55;
        public mi_page_queue_t e56;
        public mi_page_queue_t e57;
        public mi_page_queue_t e58;
        public mi_page_queue_t e59;
        public mi_page_queue_t e60;
        public mi_page_queue_t e61;
        public mi_page_queue_t e62;
        public mi_page_queue_t e63;
        public mi_page_queue_t e64;
        public mi_page_queue_t e65;
        public mi_page_queue_t e66;
        public mi_page_queue_t e67;
        public mi_page_queue_t e68;
        public mi_page_queue_t e69;
        public mi_page_queue_t e70;
        public mi_page_queue_t e71;
        public mi_page_queue_t e72;
        public mi_page_queue_t e73;
        public mi_page_queue_t e74;
    }

    public partial struct _keys_e__FixedBuffer
    {
        public nuint e0;
        public nuint e1;
    }
}
