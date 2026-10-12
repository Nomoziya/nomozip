namespace ArchivePassword.Core;

public sealed record PasswordPromptResponse(string Secret, bool Save, string Name, Guid? SavedId = null);
public sealed record ExtractionOutcome(EngineResult Engine, string OutputPath, string? Warning = null);
public sealed record TaskUpdate(string State, string Message, EngineProgress? Progress = null);
public enum ExistingFilePolicy { Ask, Skip, Replace }

public sealed class ExtractionService(IArchiveEngine engine, IPasswordVault vault, IPasswordCandidateProvider candidates)
{
    public async Task<ExtractionOutcome> ExtractAsync(string archivePath, string outputPath, bool autoTry,
        Func<EngineResult, Task<PasswordPromptResponse?>> requestPassword,
        Func<string, Task<ExistingFilePolicy>> resolveConflict,
        IProgress<TaskUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        CancellationToken skipSavedPasswordToken = default)
    {
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(destination);
        EngineResult result;
        string? secret = null, warning = null;
        Guid? savedId = null;
        PasswordPromptResponse? supplied = null;
        var currentState = "testing";
        var currentMessage = "正在检查压缩包";
        var engineProgress = new ForwardProgress<EngineProgress>(value => progress?.Report(new TaskUpdate(currentState, currentMessage, value)));
        progress?.Report(new TaskUpdate("testing", "正在检查压缩包"));
        result = await engine.TestAsync(archivePath, progress: engineProgress, cancellationToken: cancellationToken);
        if (!result.IsSuccess && result.NeedsPassword && autoTry)
        {
            IReadOnlyList<PasswordCandidate> entries;
            try { entries = await candidates.GetAsync(archivePath, cancellationToken); }
            catch (VaultException) { entries = []; warning = "密码库暂时无法读取，请手动输入密码。"; }
            for (var index = 0; index < entries.Count; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (skipSavedPasswordToken.IsCancellationRequested) break;
                currentState = "password";
                currentMessage = $"正在尝试保存的密码，{index + 1}/{entries.Count}";
                progress?.Report(new TaskUpdate(currentState, currentMessage));
                using var candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, skipSavedPasswordToken);
                result = await engine.TestAsync(archivePath, entries[index].Secret, engineProgress, candidateCancellation.Token);
                if (skipSavedPasswordToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                { result = new EngineResult { Code = "password_required" }; break; }
                if (result.IsSuccess) { secret = entries[index].Secret; savedId = entries[index].SavedId; break; }
                if (!result.NeedsPassword) break;
            }
        }
        while (!result.IsSuccess && result.NeedsPassword)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new TaskUpdate("waiting", "请输入可用密码"));
            supplied = await requestPassword(result);
            if (supplied is null) return new ExtractionOutcome(result with { Status = "cancelled", Code = "cancelled" }, destination, warning);
            secret = supplied.Secret;
            currentState = "testing"; currentMessage = "正在验证输入的密码";
            result = await engine.TestAsync(archivePath, secret, engineProgress, cancellationToken);
            if (result.IsSuccess) savedId = supplied.SavedId;
            if (!result.IsSuccess && result.NeedsPassword) progress?.Report(new TaskUpdate("waiting", "密码可能不匹配，或压缩包已损坏"));
        }
        if (!result.IsSuccess) return new ExtractionOutcome(result, destination, warning);

        var staging = Path.Combine(destination, ".archive-password-" + Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(staging).StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid staging directory.");
        Directory.CreateDirectory(staging);
        var committed = false;
        try
        {
            currentState = "extracting"; currentMessage = "正在解压";
            progress?.Report(new TaskUpdate(currentState, currentMessage));
            result = await engine.ExtractAsync(archivePath, staging, secret, engineProgress, cancellationToken);
            if (!result.IsSuccess) return new ExtractionOutcome(result, destination, warning);
            cancellationToken.ThrowIfCancellationRequested();
            var decisions = new List<(string Source, string Target, ExistingFilePolicy Policy)>();
            foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(staging, source));
                ValidateOutputPath(destination, target);
                var policy = File.Exists(target) ? await resolveConflict(target) : ExistingFilePolicy.Replace;
                if (policy == ExistingFilePolicy.Ask) return new ExtractionOutcome(new EngineResult { Status = "cancelled", Code = "cancelled" }, destination, warning);
                decisions.Add((source, target, policy));
            }
            // No final files are moved until extraction and all conflict decisions complete.
            foreach (var (source, target, policy) in decisions)
            {
                if (policy == ExistingFilePolicy.Skip) continue;
                ValidateOutputPath(destination, target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: policy == ExistingFilePolicy.Replace);
            }
            foreach (var folder in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(staging, folder));
                ValidateOutputPath(destination, target);
                Directory.CreateDirectory(target);
            }
            var skipped = decisions.Count(value => value.Policy == ExistingFilePolicy.Skip);
            if (skipped > 0) warning = $"已跳过 {skipped} 个同名文件。";
            committed = true;
            try
            {
                if (savedId is { } id) await vault.RecordSuccessAsync(id, archivePath, CancellationToken.None);
                else if (supplied?.Save == true)
                {
                    id = Guid.NewGuid();
                    var name = string.IsNullOrWhiteSpace(supplied.Name) ? Path.GetFileName(archivePath) : supplied.Name.Trim();
                    await vault.SaveAsync(new SavedPassword(id, name, supplied.Secret, "", true, DateTimeOffset.UtcNow), CancellationToken.None);
                    await vault.RecordSuccessAsync(id, archivePath, CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException)
            { warning = "解压已完成，但密码未能保存，请检查密码库。"; }
            return new ExtractionOutcome(result, destination, warning);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ExtractionOutcome(new EngineResult { Code = "output_write_failed" }, destination, "输出文件未能全部写入，请检查目标目录。已写入的文件仍在目标目录中。");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { if (committed) progress?.Report(new TaskUpdate("warning", "已完成解压，但暂存目录未能清理")); }
            }
        }
    }

    private static void ValidateOutputPath(string root, string target)
    {
        var boundary = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(target).StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new IOException("Output path escapes destination.");
        var relative = Path.GetRelativePath(root, target);
        var cursor = root;
        foreach (var component in relative.Split(Path.DirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, component);
            if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Output path crosses a link.");
        }
    }
}

public sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
