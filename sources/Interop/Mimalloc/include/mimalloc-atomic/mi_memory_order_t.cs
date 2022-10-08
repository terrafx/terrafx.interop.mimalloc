// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_memory_order_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc;

internal enum mi_memory_order_t
{
    mi_memory_order_relaxed,

    mi_memory_order_consume,

    mi_memory_order_acquire,

    mi_memory_order_release,

    mi_memory_order_acq_rel,

    mi_memory_order_seq_cst
}
