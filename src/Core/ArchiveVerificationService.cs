namespace ArchivePassword.Core;

public sealed class ArchiveVerificationOutcome
{
    public EngineResult Engine { get; }
    public string? Warning { get; }
    internal string? Secret { get; }
    internal Guid? SavedId { get; }
    internal PasswordPromptResponse? Supplied { get; }
    internal IReadOnlyDictionary<uint,PasswordPromptResponse> VerifiedPasswords { get; }
    internal ArchiveVerificationOutcome(EngineResult engine,string? secret,Guid? savedId,PasswordPromptResponse? supplied,
        IReadOnlyDictionary<uint,PasswordPromptResponse> verifiedPasswords,string? warning)
    {Engine=engine;Secret=secret;SavedId=savedId;Supplied=supplied;VerifiedPasswords=verifiedPasswords;Warning=warning;}
}

// Testing uses the same per-item password evidence as extraction, with no vault write or filesystem output API.
public sealed class ArchiveVerificationService(IArchiveEngine engine, IPasswordCandidateProvider candidates)
{
    public async Task<ArchiveVerificationOutcome> VerifyAsync(string archivePath, bool autoTry,
        Func<EngineResult, Task<PasswordPromptResponse?>> requestPassword, IProgress<TaskUpdate>? progress = null,
        CancellationToken cancellationToken = default, CancellationToken skipSavedPasswordToken = default)
    {
        EngineResult result;
        string? secret = null, warning = null;
        Guid? savedId = null;
        PasswordPromptResponse? supplied = null;
        var verifiedPasswords = new Dictionary<uint, PasswordPromptResponse>();
        var currentState = "testing";
        var currentMessage = "正在检查压缩包";
        var engineProgress = new ForwardProgress<EngineProgress>(value => progress?.Report(new TaskUpdate(currentState, currentMessage, value)));
        async Task<EngineResult> VerifyPasswordAsync(PasswordPromptResponse candidate, CancellationToken token)
        {
            var overrides = verifiedPasswords.Where(value => value.Value.Secret != candidate.Secret).ToDictionary(value => value.Key, value => value.Value.Secret);
            var tested = engine is IArchiveMultiPasswordEngine multi && overrides.Count > 0
                ? await multi.TestWithPasswordsAsync(archivePath, candidate.Secret, overrides, engineProgress, token)
                : await engine.TestAsync(archivePath, candidate.Secret, engineProgress, token);
            if (tested.Status == "cancelled") return tested;
            foreach (var failed in tested.Failures) verifiedPasswords.Remove(failed.Index);
            foreach (var index in tested.VerifiedEncryptedItems)
                if (!verifiedPasswords.ContainsKey(index)) verifiedPasswords[index] = candidate;
            return tested;
        }
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
                result = await VerifyPasswordAsync(new(entries[index].Secret, false, "", entries[index].SavedId), candidateCancellation.Token);
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
            if (supplied is null) return new ArchiveVerificationOutcome(result with { Status = "cancelled", Code = "cancelled" }, secret, savedId, supplied, verifiedPasswords, warning);
            secret = supplied.Secret;
            currentState = "testing"; currentMessage = "正在验证输入的密码";
            result = await VerifyPasswordAsync(supplied, cancellationToken);
            if (result.IsSuccess) savedId = supplied.SavedId;
            if (!result.IsSuccess && result.NeedsPassword) progress?.Report(new TaskUpdate("waiting", "密码可能不匹配，或压缩包已损坏"));
        }
        return new ArchiveVerificationOutcome(result, secret, savedId, supplied, verifiedPasswords, warning);

    }
}
