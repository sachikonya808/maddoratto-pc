using System.IO.Compression;
using RattoLauncher.Core.Models;

namespace RattoLauncher.Core.Services;

public class DownloadEngine(SetupContext context, string url, string? sha256) : ISetupStep
{
    public string Title => "Download ScarletNX";

    public Task<bool> IsDoneAsync(CancellationToken ct) => Task.FromResult(Helpers.DoFileExist(context.EngineExec));

    public async Task RunAsync(IProgress<StepProgress> progress, IProgress<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(context.GameDataDir);
        var zipPath = Path.Combine(context.GameDataDir, "engine.zip");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RattoLauncher");
        log.Report($"Downloading {url}");

        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);

            var buffer = new byte[81920];
            long done = 0;
            int n;

            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;

                if(total > 0) { progress.Report(new StepProgress($"{done / 1048576} / {total / 1048576} MB", (double)done / total.Value)); }
            }
        }

        progress.Report(new StepProgress("Extracting..."));

        if (Directory.Exists(context.EngineDir)) { Directory.Delete(context.EngineDir, recursive: true); }
        ZipFile.ExtractToDirectory(zipPath, context.EngineDir);
        File.Delete(zipPath);

        if(!Helpers.DoFileExist(context.EngineExec)) { throw new FileNotFoundException($"Expected {context.EngineExec} after extracting but it's not there."); }
    }
}
