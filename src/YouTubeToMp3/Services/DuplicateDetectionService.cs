namespace YouTubeToMp3.Services;

public enum DuplicateKind
{
    InQueue,
    InHistory,
    FileExists,
}

public sealed class DuplicateWarning
{
    public DuplicateKind Kind { get; init; }
    public string Message { get; init; } = "";
    public string? Title { get; init; }
    public string? Path { get; init; }
}

public sealed class DuplicateCheckResult
{
    public bool Ok { get; init; } = true;
    public bool AlreadyDownloaded { get; init; }
    public bool InQueue { get; init; }
    public bool InHistory { get; init; }
    public string Message { get; init; } = "";
    public string? Title { get; init; }
    public string? Path { get; init; }
}

public static class DuplicateDetectionService
{
    public static DuplicateWarning? Check(
        DownloadJob job,
        DownloadQueue queue,
        DownloadHistoryService history,
        string? predictedPath,
        IEnumerable<string>? activeUrls = null)
    {
        if (queue.ContainsUrl(job.Url) ||
            (activeUrls?.Any(u => string.Equals(u, job.Url, StringComparison.OrdinalIgnoreCase)) == true))
        {
            return new DuplicateWarning
            {
                Kind = DuplicateKind.InQueue,
                Message = "This URL is already in the queue.",
            };
        }

        var prior = history.FindByVideoId(job.Url) ?? history.FindByUrl(job.Url);
        if (prior is not null)
        {
            if (File.Exists(prior.OutputPath))
            {
                return new DuplicateWarning
                {
                    Kind = DuplicateKind.InHistory,
                    Message = $"This track was downloaded before ({prior.Title}).\nSaved to: {prior.OutputPath}",
                    Title = prior.Title,
                    Path = prior.OutputPath,
                };
            }
        }

        var expectedPath = !string.IsNullOrWhiteSpace(predictedPath)
            ? predictedPath
            : DownloadJobPathHelper.ResolveExpectedOutputPath(job);

        if (!string.IsNullOrWhiteSpace(expectedPath) && File.Exists(expectedPath))
        {
            return new DuplicateWarning
            {
                Kind = DuplicateKind.FileExists,
                Message = $"A file with this name already exists:\n{expectedPath}",
                Path = expectedPath,
            };
        }

        return null;
    }

    public static DuplicateCheckResult ToCheckResult(DuplicateWarning? warning)
    {
        if (warning is null)
        {
            return new DuplicateCheckResult
            {
                Ok = true,
                Message = "Not downloaded yet.",
            };
        }

        return new DuplicateCheckResult
        {
            Ok = true,
            AlreadyDownloaded = warning.Kind is DuplicateKind.InHistory or DuplicateKind.FileExists,
            InQueue = warning.Kind == DuplicateKind.InQueue,
            InHistory = warning.Kind == DuplicateKind.InHistory,
            Message = warning.Message,
            Title = warning.Title,
            Path = warning.Path,
        };
    }
}
