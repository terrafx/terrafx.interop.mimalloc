// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_heap_area_ex_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop
{
    // Separate struct to keep `mi_page_t` out of the public interface
    internal unsafe struct mi_heap_area_ex_t
    {
        public mi_heap_area_t area;

        public mi_page_t* page;
    }
}
