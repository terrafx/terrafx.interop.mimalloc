// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Runtime.InteropServices;

// Ported from the following files in the Open Group Base Specifications: Issue 7
// Original source is Copyright © The IEEE and The Open Group.
//  * include/sys/mman.h
//  * include/sys/resource.h
//  * include/sys/time.h
//  * include/unistd.h

namespace TerraFX.Interop.Mimalloc;

public static unsafe partial class Mimalloc
{
    private const int PR_SET_THP_DISABLE = 41;
    private const int PR_GET_THP_DISABLE = 42;
    private const int ERRNO_ENOSYS = 38;
    private const int R_OK = 4;

    [DllImport("libc", ExactSpelling = true)]
    private static extern int prctl(int option, nint arg2, nint arg3, nint arg4, nint arg5);

    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    private static extern nint syscall(nint number, void* arg1, nuint arg2, nuint arg3, nuint* arg4, nuint arg5, uint arg6);

    private static nint get_sys_mbind_number()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 237,
            Architecture.Arm64 => 235,
            _ => 0,
        };
    }

    private static nint get_sys_getcpu_number()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 309,
            Architecture.Arm64 => 168,
            Architecture.X86 => 318,
            Architecture.Arm => 345,
            _ => 0,
        };
    }

    private static void mi_unix_mem_init()
    {
        if (IsLinux && !mi_option_is_enabled(mi_option_t.mi_option_allow_thp) && (prctl(PR_GET_THP_DISABLE, 0, 0, 0, 0) == 0))
        {
            _ = prctl(PR_SET_THP_DISABLE, 1, 0, 0, 0);
        }
    }

    [DllImport("libc", ExactSpelling = true)]
    private static extern int access([NativeTypeName("const char*")] sbyte* path, int amode);

    [DllImport("libc", ExactSpelling = true)]
    private static extern int getrusage(int who, [NativeTypeName("struct rusage *")] rusage* r_usage);

    private static nuint get_path_max()
    {
        nuint path_max = 0;

        if (IsWindows)
        {
            path_max = PATH_MAX;
        }
        else
        {
            sbyte* path = stackalloc sbyte[2] { 0x2F, 0x00 };
            nint m = pathconf(path, _PC_PATH_MAX);

            if (m <= 0)
            {
                // guess
                path_max = 4096;
            }
            else if (m < 256)
            {
                // at least 256
                path_max = 256;
            }
            else if (m > 64 * 1024)
            {
                path_max = 64 * 1024;
            }
            else
            {
                path_max = (nuint)m;
            }
        }

        return path_max;
    }

    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    private static extern void* mmap(void* addr, [NativeTypeName("size_t")] nuint len, int prot, int flags, int fildes, [NativeTypeName("off_t")] nint off);

    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    private static extern int mprotect(void* addr, [NativeTypeName("size_t")] nuint len, int prot);

    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    private static extern int munmap(void* addr, [NativeTypeName("size_t")] nuint len);

    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    private static extern int madvise(void* addr, [NativeTypeName("size_t")] nuint len, int advice);

    [DllImport("libc", ExactSpelling = true)]
    [return: NativeTypeName("long")]
    private static extern nint pathconf([NativeTypeName("const char*")] sbyte* path, int name);

    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    private static extern int posix_madvise(void* addr, [NativeTypeName("size_t")] nuint len, int advice);

    [DllImport("libc", ExactSpelling = true)]
    [return: NativeTypeName("char*")]
    private static extern sbyte* realpath([NativeTypeName("const char* restrict")] sbyte* file_name, [NativeTypeName("char* restrict")] sbyte* resolved_name);

    [DllImport("libc", ExactSpelling = true)]
    [return: NativeTypeName("long")]
    private static extern nint sysconf(int name);

    private struct @rusage
    {
        [NativeTypeName("struct timeval")]
        public timeval ru_utime;

        [NativeTypeName("struct timeval")]
        public timeval ru_stime;

        [NativeTypeName("long int")]
        public nint ru_maxrss;

        [NativeTypeName("long int")]
        public nint ru_ixrss;

        [NativeTypeName("long int")]
        public nint ru_idrss;

        [NativeTypeName("long int")]
        public nint ru_isrss;

        [NativeTypeName("long int")]
        public nint ru_minflt;

        [NativeTypeName("long int")]
        public nint ru_majflt;

        [NativeTypeName("long int")]
        public nint ru_nswap;

        [NativeTypeName("long int")]
        public nint ru_inblock;

        [NativeTypeName("long int")]
        public nint ru_oublock;

        [NativeTypeName("long int")]
        public nint ru_msgsnd;

        [NativeTypeName("long int")]
        public nint ru_msgrcv;

        [NativeTypeName("long int")]
        public nint ru_nsignals;

        [NativeTypeName("long int")]
        public nint ru_nvcsw;

        [NativeTypeName("long int")]
        public nint ru_nivcsw;
    }

    private struct @timeval
    {
        [NativeTypeName("__time_t")]
        public nint tv_sec;

        [NativeTypeName("__suseconds_t")]
        public nint tv_usec;
    }
}
