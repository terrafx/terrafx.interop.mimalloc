// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_heap_area_visit_fun fnptr from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal unsafe delegate bool mi_heap_area_visit_fun([NativeTypeName("const mi_heap_t*")] mi_heap_t* heap, [NativeTypeName("const mi_heap_area_ex_t*")] mi_heap_area_ex_t* area, void* arg);
