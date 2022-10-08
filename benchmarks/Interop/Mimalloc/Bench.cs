// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using static TerraFX.Interop.Mimalloc.Mimalloc;

namespace TerraFX.Interop.Mimalloc.Benchmarks;

public unsafe class AllocatorBenchmarks
{
    [Params(100, 10_000)]
    public int Size { get; set; }

    [Params(1, 5, 40)]
    public int IterCount { get; set; }

    [Benchmark]
    public void TestMimalloc()
    {
        for (var i = 0; i < IterCount; i++)
        {
            var p = (byte*)mi_malloc((nuint)Size);
            Consume(&p);
            mi_free(p);
        }
    }
    
    [Benchmark]
    public void TestGCAlloc()
    {
        for (var i = 0; i < IterCount; i++)
        {
            var p = new byte[Size];
            Consume(ref p);
        }
    }

    [Benchmark]
    public void TestNativeAlloc()
    {
        for (var i = 0; i < IterCount; i++)
        {
            var p = (byte*)NativeMemory.Alloc((nuint)Size);
            Consume(&p);
            NativeMemory.Free(p);
        }
    }

    [Benchmark]
    public void TestAllocHGlobal()
    {
        for (var i = 0; i < IterCount; i++)
        {
            var p = (byte*)Marshal.AllocHGlobal(Size);
            Consume(&p);
            Marshal.FreeHGlobal((System.IntPtr)p);
        }
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(ref byte[] arr) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(byte** arr) { }
}
