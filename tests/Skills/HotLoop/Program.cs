using System;
using System.Threading;

namespace HotLoop;

internal static class Program
{
    private static volatile bool _stop;

    private static void Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { _stop = true; e.Cancel = true; };
        var mode = args.Length > 0 ? args[0] : "cpu";
        Console.WriteLine($"HotLoop starting in '{mode}' mode. PID={Environment.ProcessId}. Ctrl+C to stop.");
        switch (mode)
        {
            case "cpu":
                BurnCpu();
                break;
            case "alloc":
                Allocate();
                break;
            case "mixed":
                Mixed();
                break;
            default:
                Console.WriteLine("Unknown mode. Use: cpu | alloc | mixed.");
                return;
        }
    }

    private static void BurnCpu()
    {
        double acc = 0;
        long n = 0;
        while (!_stop)
        {
            acc += Math.Sqrt(n++) * Math.Sin(n) + Math.Cos(n * 0.5);
            if ((n & 0xFFFFFFF) == 0) Console.WriteLine($"n={n} acc={acc:G4}");
        }
    }

    private static void Allocate()
    {
        var sink = new System.Collections.Generic.List<byte[]>();
        long total = 0;
        while (!_stop)
        {
            for (int i = 0; i < 1000; i++)
            {
                sink.Add(new byte[1024]);
                total += 1024;
            }
            if (sink.Count > 100_000) sink.RemoveRange(0, 50_000);
            Thread.Sleep(1);
            if ((total & ((1L << 26) - 1)) < 1_000_000)
                Console.WriteLine($"live-ish={sink.Count:N0} total-allocated={total:N0}B");
        }
    }

    private static void Mixed()
    {
        var t = new Thread(BurnCpu) { IsBackground = true, Name = "CpuBurner" };
        t.Start();
        Allocate();
        t.Join(TimeSpan.FromSeconds(2));
    }
}
