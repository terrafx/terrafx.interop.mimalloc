// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_delayed_t enum from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // The delayed flags are used for efficient multi-threaded free-ing
    internal enum mi_delayed_t
    {
        // push on the owning heap thread delayed list
        MI_USE_DELAYED_FREE = 0,

        // temporary: another thread is accessing the owning heap
        MI_DELAYED_FREEING = 1,

        // optimize: push on page local thread free queue if another block is already in the heap thread delayed free list
        MI_NO_DELAYED_FREE = 2,

        // sticky, only resets on page reclaim
        MI_NEVER_DELAYED_FREE = 3,
    }
}
