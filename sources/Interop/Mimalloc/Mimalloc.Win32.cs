// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from the following files in the Windows SDK for Windows 10.0.19041.0
// Original source is Copyright © Microsoft. All rights reserved.
//  * shared/minwindef.h
//  * shared/winerror.h
//  * um/handleapi.h
//  * um/memoryapi.h
//  * um/processthreadsapi.h
//  * um/Psapi.h
//  * um/securitybaseapi.h
//  * um/sysinfoapi.h
//  * um/systemtopologyapi.h
//  * um/WinBase.h
//  * um/winnt.h

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    private const uint ERROR_INVALID_ADDRESS = 487;

    private const uint ERROR_INVALID_PARAMETER = 87;

    private const uint ERROR_SUCCESS = 0;

    private const int FALSE = 0;

    private const int MAX_PATH = 260;

    private const uint MEM_COMMIT = 0x00001000;

    private const uint MEM_DECOMMIT = 0x00004000;

    private const uint MEM_EXTENDED_PARAMETER_NONPAGED_HUGE = 0x00000010;

    private const uint MEM_LARGE_PAGES = 0x20000000;

    private const uint MEM_RELEASE = 0x00008000;

    private const uint MEM_RESERVE = 0x00002000;

    private const uint MEM_RESET = 0x00080000;

    private const uint PAGE_NOACCESS = 0x01;

    private const uint PAGE_READWRITE = 0x04;

    private const int PATH_MAX = MAX_PATH;

    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    private const uint TOKEN_QUERY = 0x0008;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

    [DllImport("advapi32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int AdjustTokenPrivileges([NativeTypeName("HANDLE")] IntPtr TokenHandle, [NativeTypeName("BOOL")] int DisableAllPrivileges, [NativeTypeName("PTOKEN_PRIVILEGES")] TOKEN_PRIVILEGES* NewState, [NativeTypeName("DWORD")] uint BufferLength, [NativeTypeName("PTOKEN_PRIVILEGES")] TOKEN_PRIVILEGES* PreviousState, [NativeTypeName("PDWORD")] uint* ReturnLength);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int CloseHandle([NativeTypeName("HANDLE")] IntPtr hObject);

    [DllImport("kernel32", ExactSpelling = true)]
    [return: NativeTypeName("HANDLE")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32", ExactSpelling = true)]
    [return: NativeTypeName("DWORD")]
    public static extern uint GetCurrentProcessorNumber();

    [DllImport("kernel32", EntryPoint = "GetFullPathNameA", ExactSpelling = true)]
    [return: NativeTypeName("DWORD")]
    public static extern uint GetFullPathName([NativeTypeName("LPCSTR")] sbyte* lpFileName, [NativeTypeName("DWORD")] uint nBufferLength, [NativeTypeName("LPSTR")] sbyte* lpBuffer, [NativeTypeName("LPSTR *")] sbyte** lpFilePart);

    [DllImport("kernel32", ExactSpelling = true)]
    [return: NativeTypeName("SIZE_T")]
    private static extern nuint GetLargePageMinimum();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NativeTypeName("DWORD")]
    private static uint GetLastError() => (uint)Marshal.GetLastWin32Error();

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    public static extern int GetNumaHighestNodeNumber([NativeTypeName("PULONG")] uint* HighestNodeNumber);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    public static extern int GetNumaNodeProcessorMask([NativeTypeName("UCHAR")] byte Node, [NativeTypeName("PULONGLONG")] ulong* ProcessorMask);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    public static extern int GetNumaProcessorNode([NativeTypeName("UCHAR")] byte Processor, [NativeTypeName("PUCHAR")] byte* NodeNumber);

    [DllImport("psapi", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int GetProcessMemoryInfo([NativeTypeName("HANDLE")] IntPtr hProcess, [NativeTypeName("PPROCESS_MEMORY_COUNTERS")] PROCESS_MEMORY_COUNTERS* ppsmemCounters, [NativeTypeName("DWORD")] uint cb);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int GetProcessTimes([NativeTypeName("HANDLE")] IntPtr hProcess, [NativeTypeName("LPFILETIME")] FILETIME* lpCreationTime, [NativeTypeName("LPFILETIME")] FILETIME* lpExitTime, [NativeTypeName("LPFILETIME")] FILETIME* lpKernelTime, [NativeTypeName("LPFILETIME")] FILETIME* lpUserTime);

    [DllImport("kernel32", ExactSpelling = true)]
    private static extern void GetSystemInfo([NativeTypeName("LPSYSTEM_INFO")] SYSTEM_INFO* lpSystemInfo);

    [DllImport("advapi32", EntryPoint = "LookupPrivilegeValueW", ExactSpelling = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int LookupPrivilegeValue([NativeTypeName("LPCWSTR")] ushort* lpSystemName, [NativeTypeName("LPCWSTR")] ushort* lpName, [NativeTypeName("PLUID")] LUID* lpLuid);

    [DllImport("advapi32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int OpenProcessToken([NativeTypeName("HANDLE")] IntPtr ProcessHandle, [NativeTypeName("DWORD")] uint DesiredAccess, [NativeTypeName("PHANDLE")] IntPtr* TokenHandle);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("LPVOID")]
    private static extern void* VirtualAlloc([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize, [NativeTypeName("DWORD")] uint flAllocationType, [NativeTypeName("DWORD")] uint flProtect);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int VirtualFree([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize, [NativeTypeName("DWORD")] uint dwFreeType);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int VirtualProtect([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize, [NativeTypeName("DWORD")] uint flNewProtect, [NativeTypeName("PDWORD")] uint* lpfOldProtect);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    [return: NativeTypeName("BOOL")]
    private static extern int VirtualUnlock([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize);

    internal enum MEM_EXTENDED_PARAMETER_TYPE
    {
        MemExtendedParameterInvalidType = 0,
        MemExtendedParameterAddressRequirements,
        MemExtendedParameterNumaNode,
        MemExtendedParameterPartitionHandle,
        MemExtendedParameterUserPhysicalHandle,
        MemExtendedParameterAttributeFlags,
        MemExtendedParameterMax,
    }

    private struct FILETIME
    {
        [NativeTypeName("DWORD")]
        public uint dwLowDateTime;

        [NativeTypeName("DWORD")]
        public uint dwHighDateTime;
    }

    private struct GROUP_AFFINITY
    {
        [NativeTypeName("KAFFINITY")]
        public nuint Mask;

        [NativeTypeName("WORD")]
        public ushort Group;

        [NativeTypeName("WORD [3]")]
        public fixed ushort Reserved[3];
    }

    private struct LUID
    {
        [NativeTypeName("DWORD")]
        public uint LowPart;

        [NativeTypeName("LONG")]
        public int HighPart;
    }

    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;

        [NativeTypeName("DWORD")]
        public uint Attributes;
    }

    private struct MEM_ADDRESS_REQUIREMENTS
    {
        [NativeTypeName("PVOID")]
        public void* LowestStartingAddress;

        [NativeTypeName("PVOID")]
        public void* HighestEndingAddress;

        [NativeTypeName("SIZE_T")]
        public nuint Alignment;
    }

    private struct MEM_EXTENDED_PARAMETER
    {
        public _Anonymous1_e__Struct Anonymous1;

        public _Anonymous2_e__Union Anonymous2;

        public partial struct _Anonymous1_e__Struct
        {
            public ulong _bitfield;

            [NativeTypeName("DWORD64 : 8")]
            public ulong Type
            {
                readonly get
                {
                    return _bitfield & 0xFFUL;
                }

                set
                {
                    _bitfield = (_bitfield & ~0xFFUL) | (value & 0xFFUL);
                }
            }

            [NativeTypeName("DWORD64 : 56")]
            public ulong Reserved
            {
                readonly get
                {
                    return (_bitfield >> 8) & 0xFFFFFFUL;
                }

                set
                {
                    _bitfield = (_bitfield & ~(0xFFFFFFUL << 8)) | ((value & 0xFFFFFFUL) << 8);
                }
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public unsafe partial struct _Anonymous2_e__Union
        {
            [FieldOffset(0)]
            [NativeTypeName("DWORD64")]
            public ulong ULong64;

            [FieldOffset(0)]
            [NativeTypeName("PVOID")]
            public void* Pointer;

            [FieldOffset(0)]
            [NativeTypeName("SIZE_T")]
            public nuint Size;

            [FieldOffset(0)]
            [NativeTypeName("HANDLE")]
            public IntPtr Handle;

            [FieldOffset(0)]
            [NativeTypeName("DWORD")]
            public uint ULong;
        }
    }

    private struct PROCESS_MEMORY_COUNTERS
    {
        [NativeTypeName("DWORD")]
        public uint cb;

        [NativeTypeName("DWORD")]
        public uint PageFaultCount;

        [NativeTypeName("SIZE_T")]
        public nuint PeakWorkingSetSize;

        [NativeTypeName("SIZE_T")]
        public nuint WorkingSetSize;

        [NativeTypeName("SIZE_T")]
        public nuint QuotaPeakPagedPoolUsage;

        [NativeTypeName("SIZE_T")]
        public nuint QuotaPagedPoolUsage;

        [NativeTypeName("SIZE_T")]
        public nuint QuotaPeakNonPagedPoolUsage;

        [NativeTypeName("SIZE_T")]
        public nuint QuotaNonPagedPoolUsage;

        [NativeTypeName("SIZE_T")]
        public nuint PagefileUsage;

        [NativeTypeName("SIZE_T")]
        public nuint PeakPagefileUsage;
    }

    private struct PROCESSOR_NUMBER
    {
        [NativeTypeName("WORD")]
        public ushort Group;

        [NativeTypeName("BYTE")]
        public byte Number;

        [NativeTypeName("BYTE")]
        public byte Reserved;
    }

    private struct SYSTEM_INFO
    {
        public _Anonymous_e__Union Anonymous;

        [NativeTypeName("DWORD")]
        public uint dwPageSize;

        [NativeTypeName("LPVOID")]
        public void* lpMinimumApplicationAddress;

        [NativeTypeName("LPVOID")]
        public void* lpMaximumApplicationAddress;

        [NativeTypeName("DWORD_PTR")]
        public nuint dwActiveProcessorMask;

        [NativeTypeName("DWORD")]
        public uint dwNumberOfProcessors;

        [NativeTypeName("DWORD")]
        public uint dwProcessorType;

        [NativeTypeName("DWORD")]
        public uint dwAllocationGranularity;

        [NativeTypeName("WORD")]
        public ushort wProcessorLevel;

        [NativeTypeName("WORD")]
        public ushort wProcessorRevision;

        [StructLayout(LayoutKind.Explicit)]
        public partial struct _Anonymous_e__Union
        {
            [FieldOffset(0)]
            [NativeTypeName("DWORD")]
            public uint dwOemId;

            [FieldOffset(0)]
            [NativeTypeName("_SYSTEM_INFO::(anonymous struct at C:/Program Files (x86)/Windows Kits/10/Include/10.0.19041.0/um/sysinfoapi.h:50:9)")]
            public _Anonymous_e__Struct Anonymous;

            public partial struct _Anonymous_e__Struct
            {
                [NativeTypeName("WORD")]
                public ushort wProcessorArchitecture;

                [NativeTypeName("WORD")]
                public ushort wReserved;
            }
        }
    }

    private struct TOKEN_PRIVILEGES
    {
        [NativeTypeName("DWORD")]
        public uint PrivilegeCount;

        [NativeTypeName("LUID_AND_ATTRIBUTES [1]")]
        public _Privileges_e__FixedBuffer Privileges;

        public partial struct _Privileges_e__FixedBuffer
        {
            public LUID_AND_ATTRIBUTES e0;
        }
    }
}
