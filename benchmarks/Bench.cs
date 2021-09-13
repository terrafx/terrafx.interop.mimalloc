using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Benchmarks
{
    public unsafe class AllocatorBenchmarks
    {
        [Params(100, 10_000)]
        public int Size { get; set; }

        [Params(1, 5, 40)]
        public int IterCount { get; set; }

        [Benchmark]
        public void TestMimAlloc()
        {
            for (int i = 0; i < IterCount; i++)
            {
                var p = (byte*)TerraFX.Interop.Mimalloc.mi_malloc((nuint)Size);
                Consume(&p);
                TerraFX.Interop.Mimalloc.mi_free(p);
            }
        }
        
        [Benchmark]
        public void TestGCAlloc()
        {
            for (int i = 0; i < IterCount; i++)
            {
                var p = new byte[Size];
                Consume(ref p);
            }
        }

        [Benchmark]
        public void TestNativeAlloc()
        {
            for (int i = 0; i < IterCount; i++)
            {
                var p = (byte*)NativeMemory.Alloc((nuint)Size);
                Consume(&p);
                NativeMemory.Free(p);
            }
        }

        [Benchmark]
        public void TestAllocHGlobal()
        {
            for (int i = 0; i < IterCount; i++)
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
}