// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_segment_queue_t struct from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

namespace TerraFX.Interop.Mimalloc
{
    // Queue of segments
    internal unsafe struct mi_segment_queue_t
    {
        public mi_segment_t* first;

        public mi_segment_t* last;
    }
}
