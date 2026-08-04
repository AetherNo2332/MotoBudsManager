using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MotoBudsManager.Services;

public sealed partial class DiagnosticLogService
{
    private const int Capacity = 250;

    public ObservableCollection<string> Entries { get; } = [];

    public void Write(string message)
    {
        var entry = $"{DateTimeOffset.Now:HH:mm:ss.fff}  {MaskAddresses(message)}";
        void AddEntry()
        {
            Entries.Add(entry);
            while (Entries.Count > Capacity)
            {
                Entries.RemoveAt(0);
            }
        }

        if (App.DispatcherQueue.HasThreadAccess)
        {
            AddEntry();
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(AddEntry);
        }
    }

    public string ExportText() => string.Join(Environment.NewLine, Entries);

    private static string MaskAddresses(string value) =>
        BluetoothAddressRegex().Replace(value, match =>
            $"{match.Groups[1].Value}:**:**:**:{match.Groups[5].Value}:{match.Groups[6].Value}");

    [GeneratedRegex(@"\b([0-9A-Fa-f]{2}):([0-9A-Fa-f]{2}):([0-9A-Fa-f]{2}):([0-9A-Fa-f]{2}):([0-9A-Fa-f]{2}):([0-9A-Fa-f]{2})\b")]
    private static partial Regex BluetoothAddressRegex();
}
