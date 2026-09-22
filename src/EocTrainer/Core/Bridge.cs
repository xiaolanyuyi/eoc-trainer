using System.Text;
using System.Text.Json;

namespace EocTrainer.Core;

/// <summary>
/// Talks to the Lua side through the two JSON documents in
/// <c>Osiris Data\EocTrainer</c>. Commands are queued and only sent one batch at
/// a time so that a slow game never misses one.
///
/// The same protocol is used whether the Lua code came from the packaged mod or
/// was injected through the Script Extender's console, which is why this class
/// does not need to know about either.
/// </summary>
public sealed class BridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new LenientNumberConverter(),
            new LenientStringConverter(),
            new LenientListConverter<string>(),
            new LenientListConverter<PartyMember>(),
            new LenientListConverter<OpResult>(),
        },
    };

    private static readonly JsonSerializerOptions CommandOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AppSettings _settings;
    private readonly List<Op> _queue = new();

    private int _inFlightSeq;
    private DateTime _inFlightSince = DateTime.MinValue;
    private DateTime _lastStateWrite = DateTime.MinValue;

    public string Token { get; }
    public int Sequence { get; private set; }
    public StateDoc? State { get; private set; }
    public string? LastError { get; private set; }
    public bool CommandInFlight { get; private set; }

    /// <summary>Sequence number of the most recently written command (0 when none was sent).</summary>
    public int LastSentSequence { get; private set; }

    public BridgeClient(AppSettings settings)
    {
        _settings = settings;
        Token = Guid.NewGuid().ToString("N")[..12];
        Sequence = Math.Max(settings.LastSequence, 0);
    }

    public bool BridgeDirectoryExists => Directory.Exists(Paths.BridgeDir);

    /// <summary>Number of operations waiting to be sent.</summary>
    public int QueuedCount => _queue.Count;

    public void Enqueue(IEnumerable<Op> ops)
    {
        _queue.AddRange(ops);
    }

    public void Enqueue(Op op)
    {
        _queue.Add(op);
    }

    /// <summary>Reads state.json when it changed since the last call.</summary>
    public void Poll()
    {
        try
        {
            if (!File.Exists(Paths.StateFile)) return;

            var write = File.GetLastWriteTimeUtc(Paths.StateFile);
            if (write == _lastStateWrite) return;

            var text = ReadAllTextShared(Paths.StateFile);
            if (string.IsNullOrWhiteSpace(text)) return;

            var state = JsonSerializer.Deserialize<StateDoc>(text, JsonOptions);
            if (state == null) return;

            _lastStateWrite = write;
            State = state;
            LastError = null;
        }
        catch (JsonException ex)
        {
            // The mod rewrites the file in place, so a partial read is possible.
            // Keep the previous snapshot, but surface the reason for diagnostics.
            LastError = "状态文件解析失败：" + ex.Message;
            _lastStateWrite = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Sends the next queued batch if the previous one has been acknowledged.</summary>
    public void Flush()
    {
        if (CommandInFlight)
        {
            var acknowledged = State != null && State.Seq >= _inFlightSeq;
            var timedOut = (DateTime.UtcNow - _inFlightSince) > TimeSpan.FromSeconds(4);
            if (!acknowledged && !timedOut) return;
            CommandInFlight = false;
        }

        if (_queue.Count == 0) return;

        Sequence++;
        var doc = new CommandDoc
        {
            Token = Token,
            Seq = Sequence,
            Host = State?.Party.FirstOrDefault(p => p.IsHost)?.Guid,
            Ops = new List<Op>(_queue),
        };
        _queue.Clear();

        try
        {
            Directory.CreateDirectory(Paths.BridgeDir);
            var json = JsonSerializer.Serialize(doc, CommandOptions);
            var temp = Paths.CommandFile + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, Paths.CommandFile, overwrite: true);

            _settings.LastSequence = Sequence;
            _settings.Save();

            _inFlightSeq = Sequence;
            _inFlightSince = DateTime.UtcNow;
            CommandInFlight = true;
            LastSentSequence = Sequence;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            CommandInFlight = false;
        }
    }

    /// <summary>Forgets the acknowledgement and sends the queue again.</summary>
    public void ResetInFlight()
    {
        CommandInFlight = false;
    }

    /// <summary>Content of the mod's own log file, or an empty string.</summary>
    public string ReadGameLog()
    {
        try
        {
            if (!File.Exists(Paths.BridgeLogFile)) return "";
            return ReadAllTextShared(Paths.BridgeLogFile);
        }
        catch
        {
            return "";
        }
    }

    public bool BridgeIsAlive()
    {
        try
        {
            if (!File.Exists(Paths.BridgeHelloFile) && State == null) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Removes the command file once the game has acknowledged it.</summary>
    public void ClearAcknowledgedCommand()
    {
        try
        {
            if (State != null && State.Seq >= Sequence && File.Exists(Paths.CommandFile))
            {
                File.Delete(Paths.CommandFile);
            }
        }
        catch
        {
            // The mod ignores the file anyway once it has been executed.
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
