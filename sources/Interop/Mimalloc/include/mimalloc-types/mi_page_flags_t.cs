// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// This file includes code based on the mi_page_flags_t union from https://github.com/microsoft/mimalloc
// The original code is Copyright © Microsoft. All rights reserved. Licensed under the MIT License (MIT).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Interop
{

    // The `in_full` and `has_aligned` page flags are put in a union to efficiently
    // test if both are false (`full_aligned == 0`) in the `mi_free` routine.
    [StructLayout(LayoutKind.Explicit)]
    internal struct mi_page_flags_t
    {
#if !MI_TSAN
        [FieldOffset(0)]
        [NativeTypeName("uint8_t")]
        public byte full_aligned;

        [FieldOffset(0)]
        public _x_e__Struct x;

        public struct _x_e__Struct
        {
            public byte _bitfield;

            [NativeTypeName("uint8_t : 1")]
            public bool in_full
            {
                get
                {
                    return (_bitfield & 0x1u) != 0;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set
                {
                    _bitfield = (byte)((_bitfield & ~0x1u) | (value ? 1u : 0u));
                }
            }

            [NativeTypeName("uint8_t : 1")]
            public bool has_aligned
            {
                get
                {
                    return ((_bitfield >> 1) & 0x1u) != 0;
                }

                set
                {
                    _bitfield = (byte)((_bitfield & ~(0x1u << 1)) | ((value ? 1u : 0u) << 1));
                }
            }
        }
#else
        // under thread sanitizer, use a byte for each flag to suppress warning, issue #130

        [FieldOffset(0)]
        [NativeTypeName("uint16_t")]
        public ushort full_aligned;

        [FieldOffset(0)]
        public _x_e__Struct x;

        public struct _x_e__Struct
        {
            [NativeTypeName("uint8_t")]
            public bool in_full;

            [NativeTypeName("uint8_t")]
            public bool has_aligned;
        }
#endif
    }
}
