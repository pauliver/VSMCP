using System;

namespace HelloCrash;

internal static class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("HelloCrash starting. About to dereference a null…");
        var mode = args.Length > 0 ? args[0] : "nre";
        switch (mode)
        {
            case "nre":
                CrashWithNullDeref();
                break;
            case "aoor":
                CrashWithIndexOutOfRange();
                break;
            case "stack":
                Recurse(0);
                break;
            default:
                Console.WriteLine($"Unknown mode '{mode}'. Use: nre | aoor | stack.");
                return;
        }
    }

    private static void CrashWithNullDeref()
    {
        string s = null;
        Console.WriteLine(s.Length);
    }

    private static void CrashWithIndexOutOfRange()
    {
        var a = new int[3];
        Console.WriteLine(a[42]);
    }

    private static int Recurse(int depth) => Recurse(depth + 1) + 1;
}
