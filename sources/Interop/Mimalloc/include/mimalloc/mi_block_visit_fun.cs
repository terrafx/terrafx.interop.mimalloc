// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_block_visit_fun fnptr from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System;

namespace TerraFX.Interop.Mimalloc
{
    public unsafe delegate bool mi_block_visit_fun([NativeTypeName("const mi_heap_t*")] IntPtr heap, [NativeTypeName("const mi_heap_area_t*")] mi_heap_area_t* area, void* block, [NativeTypeName("size_t")] nuint block_size, void* arg);
}
