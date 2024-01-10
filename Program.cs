using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MeasureHighResolutionTimer;

internal class Program
{
    private static readonly object ConsoleLock = new();
    private static double[] _deltaTimes = null!;
    private static long[] _frequency = null!;
    private static int _run = 1;
    private static int _numberOfCores;

    private static void Main(string[] args)
    {
        _numberOfCores = args.Length != 0 ? int.Parse(args[0]) : Environment.ProcessorCount;

        _deltaTimes = new double[_numberOfCores];
        _frequency = new long[_numberOfCores];

        for (var i = 0; i < _numberOfCores; i++)
        {
            var coreId = i;
            var thread = new Thread(() => MeasureOnCore(coreId));
            thread.Start();
        }

        for (;;)
        {
            DisplayAllDeltas();
            HighResolutionTimer.HighPrecisionSleep(TimeSpan.FromSeconds(1));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MeasureOnCore(int coreId)
    {
        ThreadAffinity.SetAffinity(coreId);
        var hrt = new HighResolutionTimer();
        _frequency[coreId] = HighResolutionTimer.Frequency;
        WarmUpJit(hrt);

        for (;;)
        {
            _deltaTimes[coreId] = hrt.GetDeltaTime();
            HighResolutionTimer.HighPrecisionSleep(TimeSpan.FromSeconds(1));
        }
    }

    private static void WarmUpJit(HighResolutionTimer hrt)
    {
        for (var i = 0; i < 1000000; i++) hrt.GetDeltaTime();
    }

    private static void DisplayAllDeltas()
    {
        StringBuilder sb = new();
        lock (ConsoleLock)
        {
            sb.AppendLine($"Run {_run}");
            for (var i = 0; i < _deltaTimes.Length; i++)
                sb.AppendLine($"Core {i} - Delta {_deltaTimes[i]} - Frequency {_frequency[i]}");
            Console.WriteLine(sb.ToString());
            File.AppendAllText("data.txt", sb.ToString());
            _run++;
        }
    }
}

public partial class HighResolutionTimer
{
    public static readonly long Frequency;

    private long _lastTime;

    static HighResolutionTimer()
    {
        if (!QueryPerformanceFrequency(out Frequency))
            throw new InvalidOperationException("High-performance counter not supported");
    }

    public HighResolutionTimer()
    {
        QueryPerformanceCounter(out _lastTime);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryPerformanceCounter(out long lpPerformanceCount);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryPerformanceFrequency(out long lpFrequency);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDeltaTime()
    {
        QueryPerformanceCounter(out var currentTime);
        var deltaTime = (double)(currentTime - _lastTime) / Frequency;
        _lastTime = currentTime;
        return deltaTime;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void HighPrecisionSleep(TimeSpan sleepDuration)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < sleepDuration)
            if (sleepDuration - stopwatch.Elapsed > TimeSpan.FromMilliseconds(1))
                Thread.Sleep(1);
            else
                Thread.Yield();
    }
}

public partial class ThreadAffinity
{
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();

    public static void SetAffinity(int coreId)
    {
        IntPtr affinityMask = new(1 << coreId);
        SetThreadAffinityMask(GetCurrentThread(), affinityMask);
    }
}