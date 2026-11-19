using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscImageStudio.Cd;

/// <summary>A recorder as listed by <c>cdrecord -scanbus</c>.</summary>
public sealed record CdRecordDevice(string Address, string Description)
{
    public override string ToString() => $"{Description} [{Address}]";
}

/// <summary>Front end for cdrtools' cdrecord (or wodim on Linux). IMAPI2 cannot leave an audio
/// disc open for another session, so the second-session layout burns through cdrecord:
/// session 1 (music) with <c>-multi</c>, then <c>-msinfo</c> for the exact start of session 2,
/// then the art generated for that address.</summary>
public static partial class CdRecord
{
    public const string BundledFolderName = "cdrtools";

    [GeneratedRegex(@"^\s*(?<address>\d+,\d+,\d+)\s+\d+\)\s+'(?<vendor>[^']*)'\s+'(?<product>[^']*)'\s+'(?<revision>[^']*)'\s*(?<kind>.*)$")]
    private static partial Regex ScanbusLine();

    [GeneratedRegex(@"^\s*(?<last>-?\d+)\s*,\s*(?<next>-?\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex MsinfoLine();

    /// <summary>Finds cdrecord: an explicit path first, then a <c>cdrtools</c> folder next to the
    /// program, then PATH, then common install folders. Returns null when nothing is found.</summary>
    public static string? Locate(string? configuredPath = null)
    {
        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath.Trim().Trim('"'));
        string[] names = OperatingSystem.IsWindows() ? ["cdrecord.exe"] : ["cdrecord", "wodim"];
        string baseDirectory = AppContext.BaseDirectory;
        foreach (string name in names)
        {
            candidates.Add(Path.Combine(baseDirectory, BundledFolderName, name));
            candidates.Add(Path.Combine(baseDirectory, name));
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string name in names) candidates.Add(Path.Combine(directory.Trim('"'), name));
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     }.Where(root => root.Length > 0))
            {
                candidates.Add(Path.Combine(root, "cdrtools", "cdrecord.exe"));
                candidates.Add(Path.Combine(root, "cdrtfe", "tools", "cdrtools", "cdrecord.exe"));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public static IReadOnlyList<CdRecordDevice> ParseScanbus(string output)
    {
        List<CdRecordDevice> devices = [];
        foreach (string line in output.Split('\n'))
        {
            Match match = ScanbusLine().Match(line.TrimEnd('\r'));
            if (!match.Success) continue;
            string kind = match.Groups["kind"].Value.Trim();
            // Rows with '*' are empty bus slots; scanners and disks are not recorders.
            if (kind.Contains("Disk", StringComparison.OrdinalIgnoreCase) && !kind.Contains("CD", StringComparison.OrdinalIgnoreCase)) continue;
            string description = string.Join(' ', new[] { match.Groups["vendor"].Value, match.Groups["product"].Value }
                .Select(value => value.Trim()).Where(value => value.Length > 0));
            devices.Add(new CdRecordDevice(match.Groups["address"].Value, description.Length == 0 ? kind : description));
        }

        return devices;
    }

    /// <summary>Parses <c>cdrecord -msinfo</c>: "last-session-start,next-session-start".</summary>
    public static (long LastSessionStart, long NextSessionStart) ParseMsinfo(string output)
    {
        Match match = MsinfoLine().Match(output);
        if (!match.Success)
            throw new InvalidDataException("cdrecord -msinfo did not report a session address. Is session 1 burned and the disc left open?\n" + output.Trim());
        return (long.Parse(match.Groups["last"].Value, CultureInfo.InvariantCulture),
            long.Parse(match.Groups["next"].Value, CultureInfo.InvariantCulture));
    }

    public static IReadOnlyList<string> ScanbusArguments() => ["-scanbus"];

    public static IReadOnlyList<string> MsinfoArguments(string device) => [$"dev={device}", "-msinfo"];

    /// <summary>Session 1: the songs, disc-at-once, disc left open (<c>-multi</c>).</summary>
    public static IReadOnlyList<string> SessionOneArguments(string device, CdHiddenArtPlan plan, int? speed = null,
        bool dummy = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<string> arguments = [$"dev={device}", "-v", "-sao", "-multi", "-pad"];
        if (speed is > 0) arguments.Add(string.Create(CultureInfo.InvariantCulture, $"speed={speed}"));
        if (dummy) arguments.Add("-dummy");
        arguments.Add("-audio");
        for (int index = 0; index < plan.Music.Count; index++)
        {
            // cdrecord's DAO default is a 2-second gap; pregap=0 removes it for the next track.
            if (index > 0 && plan.MusicGapSectors == 0) arguments.Add("pregap=0");
            arguments.Add(plan.Music[index].Path);
        }

        return arguments;
    }

    /// <summary>Session 2: the art track, track-at-once (its first sector lands exactly at the
    /// address <c>-msinfo</c> reported). The disc is closed unless <paramref name="leaveOpen"/>.</summary>
    public static IReadOnlyList<string> SessionTwoArguments(string device, string artWavePath, int? speed = null,
        bool leaveOpen = false, bool dummy = false)
    {
        List<string> arguments = [$"dev={device}", "-v", "-tao", "-pad"];
        if (leaveOpen) arguments.Add("-multi");
        if (speed is > 0) arguments.Add(string.Create(CultureInfo.InvariantCulture, $"speed={speed}"));
        if (dummy) arguments.Add("-dummy");
        arguments.Add("-audio");
        arguments.Add(Path.GetFullPath(artWavePath));
        return arguments;
    }

    /// <summary>A Windows batch file that burns the two sessions by hand, for people who prefer
    /// to run cdrecord themselves.</summary>
    public static string BuildSessionScript(string artWavePath, CdHiddenArtPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        StringBuilder script = new();
        script.Append("@echo off\r\n");
        script.Append("rem Disc Image Studio: music in session 1, art in session 2.\r\n");
        script.Append("rem 1. Run \"cdrecord -scanbus\" and put your recorder's address below.\r\n");
        script.Append("rem 2. Burn session 1, then check -msinfo. The second number must match\r\n");
        script.Append(CultureInfo.InvariantCulture,
            $"rem    the art's start sector, {plan.ArtStartSector} ({(plan.SessionStartMeasured ? "measured" : "estimated")}).\r\n");
        script.Append("rem    If it differs, regenerate the art with that start sector before step 3.\r\n");
        script.Append("set DEV=0,0,0\r\n");
        script.Append("set CDRECORD=cdrecord\r\n");
        script.Append("%CDRECORD% ").Append(Join(SessionOneArguments("%DEV%", plan))).Append("\r\n");
        script.Append("if errorlevel 1 goto :eof\r\n");
        script.Append("%CDRECORD% ").Append(Join(MsinfoArguments("%DEV%"))).Append("\r\n");
        script.Append("pause\r\n");
        script.Append("%CDRECORD% ").Append(Join(SessionTwoArguments("%DEV%", artWavePath))).Append("\r\n");
        return script.ToString();
    }

    public static string Join(IEnumerable<string> arguments)
        => string.Join(' ', arguments.Select(argument =>
            argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"') ? argument
                : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));

    /// <summary>Runs cdrecord, reporting each output line (progress lines end in a carriage
    /// return, so both CR and LF split lines). Cancelling kills the process; a disc being written
    /// at that moment is usually ruined.</summary>
    public static async Task<CdRecordRunResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        Action<string>? onLine = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ProcessStartInfo start = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable)) ?? Environment.CurrentDirectory,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
                                ?? throw new InvalidOperationException("Could not start " + executable);
        StringBuilder output = new();
        object gate = new();
        void Line(string line)
        {
            if (line.Length == 0) return;
            lock (gate) output.Append(line).Append('\n');
            onLine?.Invoke(line);
        }

        Task stdout = PumpAsync(process.StandardOutput, Line);
        Task stderr = PumpAsync(process.StandardError, Line);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        lock (gate) return new CdRecordRunResult(process.ExitCode, output.ToString());
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        char[] buffer = new char[4096];
        StringBuilder line = new();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                char c = buffer[index];
                if (c is '\r' or '\n')
                {
                    onLine(line.ToString());
                    line.Clear();
                }
                else
                {
                    line.Append(c);
                }
            }
        }

        if (line.Length > 0) onLine(line.ToString());
    }
}

public sealed record CdRecordRunResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}
