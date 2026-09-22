namespace EocTrainer.Core;

/// <summary>
/// A session with the Script Extender's in-game console.
///
/// Usage: <see cref="TryAttach"/> once (it attaches the calling process to the
/// game's console for as long as the session is alive), then <see cref="Send"/>
/// to type lines into it. The game console window title is set by the extender,
/// which is also how the console is located when the process id is unknown.
/// </summary>
public sealed class GameConsole : IDisposable
{
    public const string WindowTitle = "D:OS2 Script Extender Debug Console";

    private nint _input;
    private nint _output;
    private nint _window;
    private bool _attached;

    public bool Attached => _attached;
    public nint WindowHandle => _window;

    /// <summary>pid of the process that owns the console, when known.</summary>
    public uint ProcessId { get; private set; }

    /// <summary>True when a console with the extender's title exists.</summary>
    public static bool IsPresent() => ConsoleApi.FindWindowW(null, WindowTitle) != 0;

    public static uint? FindGameProcessId()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("EoCApp"))
        {
            return (uint)process.Id;
        }

        return null;
    }

    /// <summary>
    /// Attaches to the game console. Calling this from a GUI process is safe:
    /// it does not create a window, it merely joins the existing console.
    /// </summary>
    public bool TryAttach(out string error)
    {
        error = "";

        if (_attached) return true;

        var pid = FindGameProcessId();
        if (pid == null)
        {
            error = "没有找到正在运行的 EoCApp.exe";
            return false;
        }

        ConsoleApi.FreeConsole();

        if (!ConsoleApi.AttachConsole(pid.Value))
        {
            error = $"AttachConsole 失败（GetLastError={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）。" +
                    "请确认游戏已启用 CreateConsole。";
            return false;
        }

        ProcessId = pid.Value;

        _input = ConsoleApi.OpenConsoleDevice(input: true);
        _output = ConsoleApi.OpenConsoleDevice(input: false);

        if (_input == IntPtr.Zero || _output == IntPtr.Zero)
        {
            // Fall back to the standard handles in case the device names are
            // unavailable for this console.
            if (_input == IntPtr.Zero) _input = ConsoleApi.GetStdHandle(ConsoleApi.StdInputHandle);
            if (_output == IntPtr.Zero) _output = ConsoleApi.GetStdHandle(ConsoleApi.StdOutputHandle);
        }

        _window = ConsoleApi.FindWindowW(null, WindowTitle);
        _attached = true;

        if (_window == 0) _window = ConsoleApi.GetConsoleWindow();
        return true;
    }

    public void Dispose()
    {
        if (!_attached) return;
        _attached = false;

        if (_input != IntPtr.Zero) ConsoleApi.CloseHandle(_input);
        if (_output != IntPtr.Zero) ConsoleApi.CloseHandle(_output);
        _input = IntPtr.Zero;
        _output = IntPtr.Zero;

        ConsoleApi.FreeConsole();
    }

    public void HideWindow() => ShowWindow(ConsoleApi.SwHide);

    public void ShowWindow() => ShowWindow(ConsoleApi.SwShowNormal);

    private void ShowWindow(int command)
    {
        var window = _window != 0 ? _window : ConsoleApi.FindWindowW(null, WindowTitle);
        if (window != 0) ConsoleApi.ShowWindow(window, command);
    }

    public bool IsWindowVisible() =>
        ConsoleApi.IsWindowVisible(_window != 0 ? _window : ConsoleApi.FindWindowW(null, WindowTitle));

    /// <summary>Injects text into the console input buffer, followed by Enter.</summary>
    public void SendLine(string text)
    {
        if (!_attached) throw new InvalidOperationException("not attached to a console");
        WriteText(text);
        WriteKey(ConsoleApi.VirtualKeyReturn, '\r');
    }

    /// <summary>Injects text without pressing Enter.</summary>
    public void Send(string text)
    {
        if (!_attached) throw new InvalidOperationException("not attached to a console");
        WriteText(text);
    }

    private void WriteText(string text)
    {
        // Small batches: the console input buffer is a fixed size ring, so
        // pushing a whole file at once would fail with ERROR_NOT_ENOUGH_MEMORY.
        const int batchSize = 128;
        var records = new List<ConsoleApi.InputRecord>(batchSize);

        foreach (var character in text)
        {
            records.Add(MakeChar(character));
            if (records.Count < batchSize) continue;

            Write(records);
            records.Clear();
        }

        if (records.Count > 0) Write(records);
    }

    private void WriteKey(ushort virtualKey, char character)
    {
        var down = new ConsoleApi.InputRecord
        {
            EventType = ConsoleApi.KeyEventType,
            KeyEvent = new ConsoleApi.KeyEventRecord
            {
                KeyDown = 1,
                RepeatCount = 1,
                VirtualKeyCode = virtualKey,
                UnicodeChar = character,
            },
        };
        var up = down;
        up.KeyEvent.KeyDown = 0;

        Write(new List<ConsoleApi.InputRecord> { down, up });
    }

    private static ConsoleApi.InputRecord MakeChar(char character) => new()
    {
        EventType = ConsoleApi.KeyEventType,
        KeyEvent = new ConsoleApi.KeyEventRecord
        {
            KeyDown = 1,
            RepeatCount = 1,
            VirtualKeyCode = 0,
            UnicodeChar = character,
        },
    };

    private void Write(List<ConsoleApi.InputRecord> records)
    {
        var offset = 0;
        var attempts = 0;

        while (offset < records.Count)
        {
            // The console input queue is a fixed size ring buffer, so a write
            // can succeed while only consuming part of the batch.
            var remaining = records.Count - offset;
            var chunk = new ConsoleApi.InputRecord[remaining];
            records.CopyTo(offset, chunk, 0, remaining);

            if (ConsoleApi.WriteConsoleInputW(_input, chunk, (uint)remaining, out var written)
                && written > 0)
            {
                offset += (int)written;
                attempts = 0;
                continue;
            }

            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if (++attempts > 400)
            {
                throw new IOException($"写入控制台输入失败（GetLastError={error}，已写入 {offset}/{records.Count}）");
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Returns the tail of the console screen buffer (up to <paramref name="maxLines"/>
    /// rows, ending at the cursor).
    /// </summary>
    public string ReadTail(int maxLines = 40)
    {
        if (!_attached) return "";

        if (!ConsoleApi.GetConsoleScreenBufferInfo(_output, out var info)) return "";

        var width = info.Size.X;
        var cursorRow = info.CursorPosition.Y;
        if (width <= 0 || cursorRow < 0) return "";

        var firstRow = Math.Max(0, cursorRow - maxLines + 1);
        var rows = cursorRow - firstRow + 1;
        var rowBuffer = new char[width];
        var lines = new List<string>(rows);

        for (var row = 0; row < rows; row++)
        {
            var coord = new ConsoleApi.Coord(0, (short)(firstRow + row));
            if (!ConsoleApi.ReadConsoleOutputCharacterW(_output, rowBuffer, (uint)width, coord, out _))
            {
                lines.Add("");
                continue;
            }

            // A console screen buffer stores no line breaks, so each physical row
            // becomes one line; long logical lines wrap and are trimmed per row.
            lines.Add(new string(rowBuffer).Replace('\0', ' ').TrimEnd());
        }

        return string.Join('\n', lines);
    }
}
