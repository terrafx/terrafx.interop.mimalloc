// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_heap_area_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop
{
    // An area of heap space contains blocks of a single size.
    public unsafe struct mi_heap_area_t
    {
        // start of the area containing heap blocks
        public void* blocks;

        // bytes reserved for this area (virtual)
        [NativeTypeName("size_t")]
        public nuint reserved;

        // current available bytes for this area
        [NativeTypeName("size_t")]
        public nuint committed;

        // bytes in use by allocated blocks
        [NativeTypeName("size_t")]
        public nuint used;

        // size in bytes of each block
        [NativeTypeName("size_t")]
        public nuint block_size;
    }
}
