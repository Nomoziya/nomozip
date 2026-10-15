namespace ArchivePassword.Core;

public sealed record PasswordPromptResponse(string Secret, bool Save, string Name, Guid? SavedId = null);
public sealed record ExtractionOutcome(EngineResult Engine, string OutputPath, string? Warning = null);
public sealed record TaskUpdate(string State, string Message, EngineProgress? Progress = null);
public enum ExistingFilePolicy { Ask, Skip, Replace }

public sealed class ExtractionService(IArchiveEngine engine, IPasswordVault vault, IPasswordCandidateProvider candidates, IExtractionOriginReader? origin = null,ExtractionPause? pause=null)
{
    public async Task<ExtractionOutcome> ExtractAsync(string archivePath, string outputPath, bool autoTry,
        Func<EngineResult, Task<PasswordPromptResponse?>> requestPassword,
        Func<string, Task<ExistingFilePolicy>> resolveConflict,
        IProgress<TaskUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        CancellationToken skipSavedPasswordToken = default,
        Func<IReadOnlyList<ArchiveEntry>, string>? resolveDestination = null,IReadOnlyList<ArchiveEntry>? selection=null,bool recordPasswordSuccess=true,ExtractionCheckpointRequest? checkpoint=null)
    {
        var destination = Path.GetFullPath(outputPath);
        ExtractionCheckpointSession? session = null;
        try
        {
        if(checkpoint is not null && selection is not null) throw new CheckpointException("checkpoint_invalid");
        if(checkpoint?.Id is {} checkpointId)
        {
            progress?.Report(new TaskUpdate("checkpoint-checking",UiText.Get("Checkpoint.Checking")));
            session=await checkpoint.Store.ResumeAsync(checkpointId,checkpoint.Binding,cancellationToken);
            if(!ExtractionCheckpointStore.SamePath(archivePath,session.Record.ArchivePath)) throw new CheckpointException("checkpoint_source_changed");
            destination=session.Record.Destination;
            selection=session.Remaining;
            resolveDestination=null;
        }
        IExtractionOriginMark? originMark;
        try { originMark = origin is null ? null : await origin.ReadAsync(archivePath, cancellationToken); }
        catch (ExtractionOriginException error) { return new(new EngineResult { Code = error.Code }, destination, error.Message); }
        catch (OperationCanceledException) { return new(new EngineResult { Status = "cancelled", Code = "cancelled" }, destination); }
        if(pause is not null) await pause.WaitAsync(cancellationToken);
        Directory.CreateDirectory(destination);
        var verification = selection is {Count:0} && session is not null
            ? new ArchiveVerificationOutcome(new EngineResult {Status="success",Code="ok"},null,null,null,new Dictionary<uint,PasswordPromptResponse>(),null)
            : await new ArchiveVerificationService(engine, candidates).VerifyAsync(archivePath, autoTry,
            requestPassword, progress, cancellationToken, skipSavedPasswordToken,selection);
        var result = verification.Engine;
        var secret = verification.Secret;
        var savedId = verification.SavedId;
        var supplied = verification.Supplied;
        var verifiedPasswords = verification.VerifiedPasswords;
        var warning = verification.Warning;
        if (!result.IsSuccess) return new ExtractionOutcome(result, destination, warning);
        EngineResult? listed = null;
        if (resolveDestination is not null || checkpoint is not null && selection is not {Count:0})
        {
            progress?.Report(new TaskUpdate("listing", UiText.Get("T_519368274ae1")));
            if(pause is not null) await pause.WaitAsync(cancellationToken);
            listed = await engine.ListAsync(archivePath, secret, cancellationToken);
            if (!listed.IsSuccess) return new ExtractionOutcome(listed, destination, warning);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if(resolveDestination is not null) destination = Path.GetFullPath(resolveDestination(selection ?? listed.Items));
                Directory.CreateDirectory(destination);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
            { return new ExtractionOutcome(new EngineResult {Code="output_write_failed"},destination,UiText.Get("T_c6a218f534d8")); }
        }
        if(checkpoint is not null)
        {
            if(engine is not IArchiveCheckpointEngine) return new(new EngineResult {Code="unsupported_checkpoint"},destination);
            if(session is null)
            {
                progress?.Report(new TaskUpdate("checkpoint-checking",UiText.Get("Checkpoint.Checking")));
                session=await checkpoint.Store.CreateAsync(archivePath,destination,checkpoint.Binding,
                    listed!.SourcePaths.Concat(result.SourcePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),listed.Items,cancellationToken);
            }
            if(listed is not null) session.ValidateListing(listed);
            selection=session.Remaining;
        }
        var currentState = "extracting";
        var currentMessage = UiText.Get("T_09eb6850c88c");
        var engineProgress = new ForwardProgress<EngineProgress>(value => progress?.Report(new TaskUpdate(currentState, currentMessage, value)));

        var staging = session?.StagingPath ?? Path.Combine(destination, ".archive-password-" + Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(staging).StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid staging directory.");
        Directory.CreateDirectory(staging);
        var committed = false;
        try
        {
            currentState = "extracting"; currentMessage = UiText.Get("T_09eb6850c88c");
            progress?.Report(new TaskUpdate(currentState, currentMessage));
            var overrides = verifiedPasswords.Where(value => value.Value.Secret != secret).ToDictionary(value => value.Key, value => value.Value.Secret);
            if(session is not null)
            {
                if(selection!.Count>0)
                {
                    session.BeginExtraction();
                    try
                    {
                        result=await ((IArchiveCheckpointEngine)engine).ExtractCheckpointAsync(archivePath,staging,selection,secret,overrides,async (index,token)=>
                        {
                            await session.SaveCompletedAsync(index,token);
                            progress?.Report(new TaskUpdate("checkpoint",UiText.Format("Checkpoint.Saved",session.CompletedCount,session.EntryCount)));
                        },engineProgress,cancellationToken);
                    }
                    finally {session.EndExtraction();}
                }
                else result=new EngineResult {Status="success",Code="ok"};
                if(result.IsSuccess && session.Remaining.Count>0) throw new CheckpointException("checkpoint_incomplete");
            }
            else result = selection is not null
                ? engine is IArchiveSelectionEngine selectedEngine ? await selectedEngine.ExtractSelectionAsync(archivePath,staging,selection,secret,overrides,engineProgress,cancellationToken) : new EngineResult {Code="unsupported_selection"}
                : engine is IArchiveMultiPasswordEngine multi && overrides.Count > 0
                ? await multi.ExtractWithPasswordsAsync(archivePath, staging, secret, overrides, engineProgress, cancellationToken)
                : await engine.ExtractAsync(archivePath, staging, secret, engineProgress, cancellationToken);
            if (!result.IsSuccess) return new ExtractionOutcome(result, destination, warning);
            cancellationToken.ThrowIfCancellationRequested();
            var decisions = new List<(string Source, string Target, ExistingFilePolicy Policy)>();
            var pendingFiles=session is null ? Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories)
                :session.StagedFiles;
            foreach (var source in pendingFiles)
            {
                var target = Path.Combine(destination, Path.GetRelativePath(staging, source));
                ValidateOutputPath(destination, target);
                var policy = File.Exists(target) ? await resolveConflict(target) : ExistingFilePolicy.Replace;
                if (policy == ExistingFilePolicy.Ask) return new ExtractionOutcome(new EngineResult { Status = "cancelled", Code = "cancelled" }, destination, warning);
                decisions.Add((source, target, policy));
            }
            // No final files are moved until extraction and all conflict decisions complete.
            if (originMark is not null)
            {
                if(pause is not null) await pause.WaitAsync(cancellationToken);
                currentState = "origin"; currentMessage = UiText.Get("T_54036ebd63cd");
                progress?.Report(new TaskUpdate(currentState, currentMessage));
                var markedFiles=decisions.Where(value=>value.Policy!=ExistingFilePolicy.Skip).Select(value=>value.Source).Concat(session?.DeliveredFiles ?? []).ToArray();
                var retainedTimes=session is null?null:markedFiles.ToDictionary(path=>path,File.GetLastWriteTimeUtc);
                warning = CombineWarnings(warning, await originMark.ApplyAsync(markedFiles, cancellationToken));
                if(retainedTimes is not null) foreach(var (path,time) in retainedTimes) File.SetLastWriteTimeUtc(path,time);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if(session is not null)
                foreach(var decision in decisions) await session.SetIntentAsync(decision.Source,decision.Policy,cancellationToken);
            foreach (var (source, target, policy) in decisions)
            {
                if(pause is not null) await pause.WaitAsync(cancellationToken);
                if (policy == ExistingFilePolicy.Skip) continue;
                await using var delivery=session is null?null:await session.VerifyForDeliveryAsync(source,cancellationToken);
                ValidateOutputPath(destination, target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: policy == ExistingFilePolicy.Replace);
                if(session is not null)
                {
                    await session.SetDeliveredAsync(target,CancellationToken.None);
                    progress?.Report(new TaskUpdate("checkpoint-commit",UiText.Get("Checkpoint.Committing")));
                }
            }
            var pendingFolders=session is null ? Directory.EnumerateDirectories(staging,"*",SearchOption.AllDirectories)
                :session.Record.Entries.Where(entry=>entry.Directory).Select(session.Source);
            foreach (var folder in pendingFolders)
            {
                var target = Path.Combine(destination, Path.GetRelativePath(staging, folder));
                ValidateOutputPath(destination, target);
                Directory.CreateDirectory(target);
            }
            var skipped = session?.Record.Receipts.Count(value=>value.State=="skipped") ?? decisions.Count(value => value.Policy == ExistingFilePolicy.Skip);
            if (skipped > 0) warning = CombineWarnings(warning, UiText.Format("T_33624dd36711", skipped));
            committed = true;
            try
            {
                var usedPasswords = verifiedPasswords.Where(value => result.VerifiedEncryptedItems.Contains(value.Key)).Select(value => value.Value).DistinctBy(value => value.Secret, StringComparer.Ordinal).ToArray();
                // Engines without per-item evidence keep the established single-password behavior.
                if (verifiedPasswords.Count == 0 && secret is not null) usedPasswords = [supplied ?? new(secret, false, "", savedId)];
                foreach (var used in recordPasswordSuccess?usedPasswords:[])
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
            { warning = CombineWarnings(warning, UiText.Get("T_920a69aef13d")); }
            if(session is not null)
            {
                try {await session.CompleteAsync(CancellationToken.None);}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {warning=CombineWarnings(warning,UiText.Format("Checkpoint.Cleanup",session.StagingRoot));}
            }
            return new ExtractionOutcome(result, destination, warning);
        }
        catch(CheckpointException) {throw;}
        catch (ExtractionOriginException error)
        { return new(new EngineResult { Code = error.Code }, destination, CombineWarnings(warning, error.Message)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ExtractionOutcome(new EngineResult { Code = "output_write_failed" }, destination, UiText.Get("T_ee5907bc1d41"));
        }
        finally
        {
            if (session is null && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { if (committed) progress?.Report(new TaskUpdate("warning", UiText.Get("T_369f392371ab"))); }
            }
        }
        }
        catch(CheckpointException error)
        {return new(new EngineResult {Code=error.Code},destination,UiText.Get("Checkpoint.Error."+error.Code));}
        catch(OperationCanceledException) when(checkpoint is not null)
        {return new(new EngineResult {Status="cancelled",Code="cancelled"},destination);}
        finally {if(session is not null) await session.DisposeAsync();}
    }

    private static string? CombineWarnings(string? current, string? added) => string.IsNullOrEmpty(added) ? current : string.IsNullOrEmpty(current) ? added : current + "\n" + added;

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
