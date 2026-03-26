using System.Text;

namespace AutoQuestConflictPatcher.Reporting;

public sealed class MergeReport
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public void Log(string line)
    {
        lock (_gate)
        {
            _lines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        }
    }

    public void WriteTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        lock (_gate)
        {
            File.WriteAllText(path, string.Join(Environment.NewLine, _lines), Encoding.UTF8);
        }
    }
}
