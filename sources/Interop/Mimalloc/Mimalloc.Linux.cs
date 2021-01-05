// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Runtime.InteropServices;

// Ported from the following files in the Open Group Base Specifications: Issue 7
// Original source is Copyright © The IEEE and The Open Group.
//  * include/sys/mman.h
//  * include/sys/resource.h
//  * include/sys/time.h
//  * include/unistd.h

namespace TerraFX.Interop
{
    public static unsafe partial class Mimalloc
    {
        private const int _PC_PATH_MAX = 4;

        private const int _SC_PAGESIZE = 30;

        private const int HUGETLB_FLAG_ENCODE_1GB = 30 << HUGETLB_FLAG_ENCODE_SHIFT;

        private const int HUGETLB_FLAG_ENCODE_2MB = 21 << HUGETLB_FLAG_ENCODE_SHIFT;

        private const int HUGETLB_FLAG_ENCODE_SHIFT = 26;

        private const int RUSAGE_SELF = 0;

        private const int MADV_DONTNEED = 4;

        private const int MADV_FREE = 8;

        private const int MADV_HUGEPAGE = 14;

        private const int MAP_ANON = MAP_ANONYMOUS;

        private const int MAP_ANONYMOUS = 0x20;

        private const int MAP_FIXED = 0x10;

        private const int MAP_PRIVATE = 0x02;

        private const int MAP_HUGE_1GB = HUGETLB_FLAG_ENCODE_1GB;

        private const int MAP_HUGE_2MB = HUGETLB_FLAG_ENCODE_2MB;

        private const int MAP_HUGETLB = 0x040000;

        private const int MAP_NORESERVE = 0x4000;

        private const nuint MPOL_PREFERRED = 1;

        private const int PROT_NONE = 0x0;

        private const int PROT_READ = 0x1;

        private const int PROT_WRITE = 0x2;

        private const int R_OK = 4;

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

        private struct rusage
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

        private struct timeval
        {
            [NativeTypeName("__time_t")]
            public nint tv_sec;

            [NativeTypeName("__suseconds_t")]
            public nint tv_usec;
        }
    }
}
