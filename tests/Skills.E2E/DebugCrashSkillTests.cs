using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Server;   // DbgEngInvoker — headless, out-of-process dump inspection
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// DebugCrash skill e2e: capture a live process dump via <c>dump.save</c>, then validate it
/// HEADLESSLY — minidump signature plus a module listing through cdb.exe (DbgEng).
///
/// This test deliberately does NOT drive VS's dump-debugging UI (<c>dump.open</c> /
/// <c>dump.summary</c>). Opening a dump runs <c>Debug.Start</c> on the VS main thread, which can pop
/// a modal dialog and wedge the entire VSIX message pump indefinitely — fatal in an unattended test
/// (it hung a run for two hours). <see cref="DbgEngInvoker"/> shells out to <c>cdb.exe -z</c>, which
/// is fully out-of-process and time-boxed, so it can never block Visual Studio.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DebugCrashSkillTests
{
    private readonly E2EFixture _f;
    public DebugCrashSkillTests(E2EFixture f) => _f = f;

    [SkippableFact]
    public async Task DebugCrash_dump_save_produces_loadable_dump_with_modules()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);

        var rpc = await _f.ConnectAsync();

        var helloCrashDir = Path.Combine(_f.FixturesRoot, "HelloCrash");
        Skip.IfNot(Directory.Exists(helloCrashDir), $"Missing fixture: {helloCrashDir}");

        // Always build so the launched exe reflects current sample source (e.g. the bounded "idle" mode).
        await BuildAsync(helloCrashDir);
        var exe = Path.Combine(helloCrashDir, "bin", "Debug", "net9.0", "HelloCrash.exe");
        Skip.IfNot(File.Exists(exe), $"Built apphost not found: {exe}");

        // Launch the apphost DIRECTLY in "idle" mode so dump.save has a settled, live target.
        // `dotnet run` would fork — proc.Id would be the launcher, not the app.
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "idle",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3)); // let the runtime settle

            var dumpPath = Path.Combine(Path.GetTempPath(), $"vsmcp-e2e-{Guid.NewGuid():N}.dmp");
            var save = await rpc.DumpSaveAsync(new DumpSaveOptions { Pid = proc.Id, Path = dumpPath, Full = true });
            Assert.True(save.BytesWritten > 0, "dump.save produced an empty dump");
            Assert.True(File.Exists(save.Path), $"dump.save reported success but the file is missing: {save.Path}");

            try
            {
                // (1) Cheap, dependency-free: every real minidump starts with the 'MDMP' signature.
                Assert.Equal("MDMP", ReadMagic(save.Path));

                // (2) Deeper, still headless: enumerate modules with cdb (`lm`). cdb is out-of-process
                // and time-boxed by DbgEngInvoker, so it can never wedge VS. Skip if the Debugging
                // Tools for Windows aren't installed on this machine.
                try
                {
                    var dbg = await DbgEngInvoker.RunAsync(
                        new DumpDbgEngOptions { DumpPath = save.Path, Command = "lm", TimeoutMs = 90_000 },
                        CancellationToken.None);
                    Assert.False(dbg.TimedOut, "cdb 'lm' timed out reading the dump");

                    // ntdll is mapped into every Windows process image — its presence proves cdb
                    // loaded the dump and read its module-list stream.
                    Assert.Contains("ntdll", dbg.Output ?? "", StringComparison.OrdinalIgnoreCase);
                }
                catch (FileNotFoundException)
                {
                    Skip.If(true, "cdb.exe (Windows SDK Debugging Tools) not installed; headless module check skipped.");
                }
            }
            finally
            {
                try { File.Delete(save.Path); } catch { }
            }
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        }
    }

    private static string ReadMagic(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> buf = stackalloc byte[4];
        int n = fs.Read(buf);
        return n == 4 ? System.Text.Encoding.ASCII.GetString(buf) : "";
    }

    private static async Task BuildAsync(string projectDir)
    {
        using var build = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"build \"{projectDir}\" -c Debug --nologo")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        build.Start();
        // Drain both pipes concurrently so a chatty build can't deadlock on a full pipe buffer.
        var so = build.StandardOutput.ReadToEndAsync();
        var se = build.StandardError.ReadToEndAsync();
        build.WaitForExit(180_000);
        await Task.WhenAll(so, se);
    }
}
