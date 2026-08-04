using Mdma.Core;

namespace Mdma.Cli;

public sealed class ConsoleProgressReporter : IProgress<OperationProgress>
{
    private readonly bool _isJson;
    private readonly bool _isRedirected;
    private string? _lastStage;

    public ConsoleProgressReporter(bool isJson)
    {
        _isJson = isJson;
        _isRedirected = Console.IsOutputRedirected;
    }

    public void Report(OperationProgress progress)
    {
        if (_isJson)
            return;

        if (_isRedirected)
        {
            if (progress.Stage != _lastStage)
            {
                _lastStage = progress.Stage;
                Console.WriteLine($"[PROGRESS] {progress.Stage}");
            }
            return;
        }

        var pctText = progress.PercentComplete.HasValue
            ? $" ({progress.PercentComplete.Value:F0}%)"
            : "";
        var detailText = !string.IsNullOrEmpty(progress.Detail) ? $" - {progress.Detail}" : "";
        Console.Write($"\r\x1b[K[PROGRESS] {progress.Stage}{pctText}{detailText}");
    }

    public void Complete()
    {
        if (!_isJson && !_isRedirected)
        {
            Console.WriteLine();
        }
    }
}
