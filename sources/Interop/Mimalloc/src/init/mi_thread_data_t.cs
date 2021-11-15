// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_thread_data_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // note: in x64 in release build `sizeof(mi_thread_data_t)` is under 4KiB (= OS page size).
    internal struct mi_thread_data_t
    {
        // must come first due to cast in `_mi_heap_done`
        public mi_heap_t heap;

        public mi_tld_t tld;
    }
}
