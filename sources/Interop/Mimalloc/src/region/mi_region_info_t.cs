// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_region_info_t union from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.InteropServices;

namespace TerraFX.Interop
{
    // Region info
    [StructLayout(LayoutKind.Explicit)]
    internal struct mi_region_info_t
    {
        [FieldOffset(0)]
        [NativeTypeName("uintptr_t")]
        public nuint value;

        [FieldOffset(0)]
        public _x_e__Struct x;

        public struct _x_e__Struct
        {
            // initialized?
            public bool valid;

            // allocated in fixed large/huge OS pages
            public bool is_large;

            // the associated NUMA node (where -1 means no associated node)
            public short numa_node;
        }
    }
}
