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
        CancellationToken cancellationToken = default, CancellationToken skipSavedPasswordToken = default,IReadOnlyList<ArchiveEntry>? selection=null)
    {
        EngineResult result;
        string? secret = null, warning = null;
        Guid? savedId = null;
        PasswordPromptResponse? supplied = null;
        var verifiedPasswords = new Dictionary<uint, PasswordPromptResponse>();
        var currentState = "testing";
        var currentMessage = UiText.Get("T_70b6cd556861");
        var engineProgress = new ForwardProgress<EngineProgress>(value => progress?.Report(new TaskUpdate(currentState, currentMessage, value)));
        Task<EngineResult> TestAsync(string? password,IReadOnlyDictionary<uint,string> overrides,CancellationToken token) =>selection is not null
            ? engine is IArchiveSelectionEngine selectedEngine ? selectedEngine.TestSelectionAsync(archivePath,selection,password,overrides,engineProgress,token) : Task.FromResult(new EngineResult {Code="unsupported_selection"})
            : engine is IArchiveMultiPasswordEngine multi && overrides.Count>0 ? multi.TestWithPasswordsAsync(archivePath,password,overrides,engineProgress,token) : engine.TestAsync(archivePath,password,engineProgress,token);
        async Task<EngineResult> VerifyPasswordAsync(PasswordPromptResponse candidate, CancellationToken token)
        {
            var overrides = verifiedPasswords.Where(value => value.Value.Secret != candidate.Secret).ToDictionary(value => value.Key, value => value.Value.Secret);
            var tested = await TestAsync(candidate.Secret,overrides,token);
            if (tested.Status == "cancelled") return tested;
            foreach (var failed in tested.Failures) verifiedPasswords.Remove(failed.Index);
            foreach (var index in tested.VerifiedEncryptedItems)
                if (!verifiedPasswords.ContainsKey(index)) verifiedPasswords[index] = candidate;
            return tested;
        }
        progress?.Report(new TaskUpdate("testing", UiText.Get("T_70b6cd556861")));
        result = await TestAsync(null,new Dictionary<uint,string>(),cancellationToken);
        if (!result.IsSuccess && result.NeedsPassword && autoTry)
        {
            IReadOnlyList<PasswordCandidate> entries;
            try { entries = await candidates.GetAsync(archivePath, cancellationToken); }
            catch (VaultException error) { entries = []; warning = error.Code == "vault_locked" ? UiText.Get("T_c1043fe70086") : UiText.Get("T_c132d4b6ff48"); }
            for (var index = 0; index < entries.Count; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (skipSavedPasswordToken.IsCancellationRequested) break;
                currentState = "password";
                currentMessage = UiText.Format("T_c124c00caa4f", index + 1, entries.Count);
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
            progress?.Report(new TaskUpdate("waiting", UiText.Get("T_aa41cd7ea619")));
            supplied = await requestPassword(result);
            if (supplied is null) return new ArchiveVerificationOutcome(result with { Status = "cancelled", Code = "cancelled" }, secret, savedId, supplied, verifiedPasswords, warning);
            secret = supplied.Secret;
            currentState = "testing"; currentMessage = UiText.Get("T_144cae45601e");
            result = await VerifyPasswordAsync(supplied, cancellationToken);
            if (result.IsSuccess) savedId = supplied.SavedId;
            if (!result.IsSuccess && result.NeedsPassword) progress?.Report(new TaskUpdate("waiting", UiText.Get("T_1554396888e3")));
        }
        return new ArchiveVerificationOutcome(result, secret, savedId, supplied, verifiedPasswords, warning);

    }
}
