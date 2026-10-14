using System.Text;

namespace SchematicsAgent;

/// <summary>
/// Copies everything the server prints (its own messages and errors) into server.log next to the models, with the
/// time, so there's a full record even when Unity only shows part of it or has lost track of the server.
/// </summary>
public sealed class ServerLog : TextWriter
{
    private static readonly object Gate = new();
    private static StreamWriter? _file;

    private readonly TextWriter _console;
    private readonly bool _error;
    private readonly StringBuilder _line = new();

    private ServerLog(TextWriter console, bool error)
    {
        _console = console;
        _error = error;
    }

    public override Encoding Encoding => _console.Encoding;

    public static void Start(string folder)
    {
        try
        {
            _file = new StreamWriter(Path.Combine(folder, "server.log"), append: false, Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(new ServerLog(Console.Out, error: false));
            Console.SetError(new ServerLog(Console.Error, error: true));
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine($"[server] Crashed: {e.ExceptionObject}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[server] Couldn't write server.log: {exception.Message}");
        }
    }

    public override void Write(char value)
    {
        lock (Gate)
        {
            _console.Write(value);
            if (value == '\n')
            {
                string text = _line.ToString().TrimEnd('\r');
                _line.Clear();
                try { _file?.WriteLine($"[{DateTime.Now:HH:mm:ss}]{(_error ? " !" : "")} {text}"); }
                catch { }
                _console.Flush();
            }
            else
                _line.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        lock (Gate)
            foreach (char c in value)
                Write(c);
    }

    public override void WriteLine(string? value)
    {
        lock (Gate)
        {
            Write(value);
            Write('\n');
        }
    }
}
