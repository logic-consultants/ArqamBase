using System.Diagnostics;

namespace ArqamBase.App.Core;

/// <summary>
/// Optional timing log for finding slow steps. On only when ARQAMBASE_TIMING=1; writes to %TEMP%\ArqamBase-timing.log.
/// Usage: using var t = Timing.Step("Students: load"); … t.Mark("families");
/// </summary>
public sealed class Timing : IDisposable
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("ARQAMBASE_TIMING") == "1";
    public static bool On => Enabled;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ArqamBase-timing.log");
    private readonly string _name;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Stopwatch _lap = Stopwatch.StartNew();

    private Timing(string name) => _name = name;

    public static Timing Step(string name) => new(name);

    public void Mark(string what)
    {
        if (!Enabled) return;
        Write($"{_name} | {what}: {_lap.ElapsedMilliseconds} ms");
        _lap.Restart();
    }

    public void Dispose()
    {
        if (Enabled) Write($"{_name} | TOTAL: {_total.ElapsedMilliseconds} ms");
    }

    private static void Write(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }
}

