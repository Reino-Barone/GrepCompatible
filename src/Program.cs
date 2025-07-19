using GrepCompatible.Core;

// Ctrl+Cのハンドリング
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var app = GrepApplication.CreateDefault();
    var exitCode = await app.RunAsync(args, cts.Token).ConfigureAwait(false);
    Environment.ExitCode = exitCode;
}
catch (OperationCanceledException)
{
    Environment.ExitCode = 130; // SIGINT
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}").ConfigureAwait(false);
    Environment.ExitCode = 2;
}
