// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_page_queue_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // Pages of a certain block size are held in a queue.
    internal unsafe struct mi_page_queue_t
    {
        public mi_page_t* first;

        public mi_page_t* last;

        [NativeTypeName("size_t")]
        public nuint block_size;
    }
}
