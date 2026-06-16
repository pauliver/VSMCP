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
            case "idle":
                // Stay alive (bounded) so dump.save / attach tooling has a settled, live target.
                // Self-exits after a couple of minutes so a test killed before teardown can't leave a
                // zombie holding a file lock on the built exe (which would block the next build).
                Console.WriteLine("idle: sleeping (bounded) until killed.");
                System.Threading.Thread.Sleep(TimeSpan.FromMinutes(2));
                break;
            default:
                Console.WriteLine($"Unknown mode '{mode}'. Use: nre | aoor | stack | idle.");
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
