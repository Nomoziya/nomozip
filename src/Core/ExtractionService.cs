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
        CancellationToken skipSavedPasswordToken = default,
        Func<IReadOnlyList<ArchiveEntry>, string>? resolveDestination = null)
    {
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(destination);
        var verification = await new ArchiveVerificationService(engine, candidates).VerifyAsync(archivePath, autoTry,
            requestPassword, progress, cancellationToken, skipSavedPasswordToken);
        var result = verification.Engine;
        var secret = verification.Secret;
        var savedId = verification.SavedId;
        var supplied = verification.Supplied;
        var verifiedPasswords = verification.VerifiedPasswords;
        var warning = verification.Warning;
        if (!result.IsSuccess) return new ExtractionOutcome(result, destination, warning);
        if (resolveDestination is not null)
        {
            progress?.Report(new TaskUpdate("listing", "正在确定解压目录"));
            var listed = await engine.ListAsync(archivePath, secret, cancellationToken);
            if (!listed.IsSuccess) return new ExtractionOutcome(listed, destination, warning);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                destination = Path.GetFullPath(resolveDestination(listed.Items));
                Directory.CreateDirectory(destination);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
            { return new ExtractionOutcome(new EngineResult {Code="output_write_failed"},destination,"无法建立解压目录，请检查同名文件和访问权限。"); }
        }
        var currentState = "extracting";
        var currentMessage = "正在解压";
        var engineProgress = new ForwardProgress<EngineProgress>(value => progress?.Report(new TaskUpdate(currentState, currentMessage, value)));

        var staging = Path.Combine(destination, ".archive-password-" + Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(staging).StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid staging directory.");
        Directory.CreateDirectory(staging);
        var committed = false;
        try
        {
            currentState = "extracting"; currentMessage = "正在解压";
            progress?.Report(new TaskUpdate(currentState, currentMessage));
            var overrides = verifiedPasswords.Where(value => value.Value.Secret != secret).ToDictionary(value => value.Key, value => value.Value.Secret);
            result = engine is IArchiveMultiPasswordEngine multi && overrides.Count > 0
                ? await multi.ExtractWithPasswordsAsync(archivePath, staging, secret, overrides, engineProgress, cancellationToken)
                : await engine.ExtractAsync(archivePath, staging, secret, engineProgress, cancellationToken);
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
                var usedPasswords = verifiedPasswords.Where(value => result.VerifiedEncryptedItems.Contains(value.Key)).Select(value => value.Value).DistinctBy(value => value.Secret, StringComparer.Ordinal).ToArray();
                // Engines without per-item evidence keep the established single-password behavior.
                if (verifiedPasswords.Count == 0 && secret is not null) usedPasswords = [supplied ?? new(secret, false, "", savedId)];
                foreach (var used in usedPasswords)
                {
                    if (used.SavedId is { } id) await vault.RecordSuccessAsync(id, archivePath, CancellationToken.None);
                    else if (used.Save)
                    {
                        id = Guid.NewGuid();
                        var name = string.IsNullOrWhiteSpace(used.Name) ? Path.GetFileName(archivePath) : used.Name.Trim();
                        await vault.SaveAsync(new SavedPassword(id, name, used.Secret, "", true, DateTimeOffset.UtcNow), CancellationToken.None);
                        await vault.RecordSuccessAsync(id, archivePath, CancellationToken.None);
                    }
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
