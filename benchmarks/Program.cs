// See https://aka.ms/new-console-template for more information

using Benchmarks;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<AllocatorBenchmarks>();
