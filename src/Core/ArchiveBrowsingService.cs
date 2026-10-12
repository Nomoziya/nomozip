namespace ArchivePassword.Core;

public sealed record ArchiveBrowseOutcome(EngineResult Engine, string? Warning = null);

// Reading a header is not proof that encrypted file data can be extracted.
// This service has no vault write API and does not test or extract file contents.
public sealed class ArchiveBrowsingService(IArchiveEngine engine, IPasswordCandidateProvider candidates)
{
    public async Task<ArchiveBrowseOutcome> BrowseAsync(string archivePath, bool autoTry,
        Func<Task<string?>> requestPassword, IProgress<TaskUpdate>? progress = null,
        CancellationToken cancellationToken = default, CancellationToken skipSavedPasswordToken = default)
    {
        string? warning = null;
        progress?.Report(new TaskUpdate("listing", "正在读取压缩包内容"));
        var result = await engine.ListAsync(archivePath, cancellationToken: cancellationToken);
        if (!result.IsSuccess && result.NeedsPassword && autoTry)
        {
            IReadOnlyList<PasswordCandidate> saved;
            try { saved = await candidates.GetAsync(archivePath, cancellationToken); }
            catch (VaultException) { saved = []; warning = "密码库暂时无法读取，请手动输入密码。"; }
            for (var index = 0; index < saved.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (skipSavedPasswordToken.IsCancellationRequested) break;
                progress?.Report(new TaskUpdate("password", $"正在尝试保存的密码，{index + 1}/{saved.Count}"));
                using var candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, skipSavedPasswordToken);
                result = await engine.ListAsync(archivePath, saved[index].Secret, candidateCancellation.Token);
                if (skipSavedPasswordToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                { result = new EngineResult { Code = "password_required" }; break; }
                if (result.IsSuccess || !result.NeedsPassword) break;
            }
        }
        while (!result.IsSuccess && result.NeedsPassword)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new TaskUpdate("waiting", "请输入密码以查看内容"));
            var secret = await requestPassword();
            if (secret is null) return new(new EngineResult { Status = "cancelled", Code = "cancelled" }, warning);
            result = await engine.ListAsync(archivePath, secret, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, warning);
    }
}
