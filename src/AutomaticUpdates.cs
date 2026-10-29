using BetterAstralParty.Observability;
using BetterAstralParty.Updating;
namespace BetterAstralParty;
internal enum AutomaticUpdateStatus { NotConfigured, Idle, MissingAssets, Downloading, Preparing, Ready, Queued, Cancelling, Cancelled, CancelFailed, CheckingInstallation, RecoveryRequired, FilesBusy, AlreadyClaimed, ManualUpgradeRequired, Failed, Committed, RetryWaiting, FaultDisabled, FaultStorageFailed, Reactivating, TransitionRequired }
internal sealed class AutomaticUpdateException(AutomaticUpdateStatus status) : Exception { internal AutomaticUpdateStatus Status { get; } = status; }
internal interface IPreparedUpdate { string Stage { get; } }
internal interface IUpdateHelperJob : IDisposable { AutomaticUpdateStatus? Poll(); DiagnosticOperation DiagnosticContextOperation => default; string? DiagnosticTargetVersion => null; }
internal interface IUpdateHandoff {
    bool Configured { get; }
    Task<object> Prepare(DownloadedUpdate update, UpdateContext context, CancellationToken token);
    Task<object> Queue(object prepared, CancellationToken token);
    void SignalCancel(object prepared);
    void SignalFailure(object prepared) => SignalCancel(prepared);
    Task Cancel(object prepared);
    Task<string?> Inspect();
}
// Main-thread state only. Disk, settings, network and launch run on workers.
internal sealed class AutomaticUpdates : IDisposable {
    private sealed record Pending(Task<object> Task, CancellationTokenSource Cancellation, int Generation, bool Queue);
    private sealed record FaultOutcome(UpdateFault? Fault, bool StorageFailed);
    private readonly UpdateDownloads _downloads;
    private readonly IUpdateHandoff? _handoff;
    private readonly IUpdateSafety? _safety;
    private IReleaseAuthentication? _authentication;
    private string _identity = "", _requestIdentity = "", _autoSeen = "";
    private UpdateDownloadRequest? _request;
    private UpdateDownloadRequest? _explicitTransition;
    private int _transitionSourceGeneration;
    private bool _applyOnce;
    private ReceiptChannelState? _channels;
    private string? _installedVersion;
    private bool _requiresTransition, _transitionCandidate;
    private Pending? _pending;
    private object? _ready;
    private IUpdateHelperJob? _job;
    private int _generation, _attempts, _sourceGeneration, _busyRetries;
    private bool _configuredOnce;
    private Task? _cancel, _completion, _reactivation;
    private UpdateContext? _retiredContext;
    private string? _retiredStage;
    private Task<UpdateFault>? _begin;
    private UpdateFault? _operation;
    private DiagnosticOperation _diagnosticAttempt;
    private string? _diagnosticAttemptTarget;
    private Task<UpdateSafetySnapshot>? _inspection;
    private Task<FaultOutcome>? _failure;
    private DateTimeOffset _retryAt;
    private bool _blocked, _recovery, _disposed, _contextAfterCancel, _preferencesRepair;
    private ReleaseChannel? _deferredChannel;
    internal UpdateFault? Fault { get; private set; }
    internal string? RecoveryStage { get; private set; }
    internal bool AutoDownload { get; private set; } = true;
    internal bool ApplyAfterExit { get; private set; } = true;
    internal AutomaticUpdateStatus Status { get; private set; } = AutomaticUpdateStatus.NotConfigured;
    internal int Revision { get; private set; }
    internal AutomaticUpdateSnapshot Snapshot => new(_generation, _sourceGeneration, _requestIdentity, _downloads.Feed?.Identity ?? "",
        _request?.Context.Repository ?? "", _request?.Context.RepositoryId ?? 0, _request?.Context.Channel ?? "", _request?.Context.ReleaseTag ?? "",
        _request?.Context.ReleaseId ?? 0, _request?.Context.AssetId ?? 0, _request?.DescriptorAssetId ?? 0, _request?.SignatureAssetId ?? 0,
        Status, CanDownload, CanCancel, _request?.Context.Transition != null, _channels?.ActiveChannel ?? "", _installedVersion ?? "", _requiresTransition);
    internal void SetChannel(ReleaseChannel channel) {
        // A running apply keeps its status and target; the switch follows once it ends.
        if (Applying) { _deferredChannel = channel; return; }
        _deferredChannel = null;
        if (_downloads.Feed?.Channel == channel.ToString()) return;
        Retire(true); _downloads.SelectFeed(channel); _downloads.Configure(_authentication); _identity = Identity;
        RefreshTransition();
        if (!_blocked && _inspection == null) Set(Configured ? _cancel != null ? AutomaticUpdateStatus.Cancelling : _requiresTransition ? AutomaticUpdateStatus.TransitionRequired : AutomaticUpdateStatus.Idle : AutomaticUpdateStatus.NotConfigured);
    }
    internal bool AuthenticationLost => _downloads.Result.Status is UpdateDownloadStatus.AuthenticationRequired or UpdateDownloadStatus.AccessUnavailable;
    internal bool AccessLost => _downloads.Result.Status == UpdateDownloadStatus.AccessUnavailable;
    private bool Configured => !_disposed && _downloads.IsConfigured && _handoff?.Configured == true;
    internal bool CanDownload => Configured && !_blocked && !_requiresTransition && Status != AutomaticUpdateStatus.Committed && _request != null && _pending == null && _cancel == null && _begin == null && _completion == null && _inspection == null && Status is not (AutomaticUpdateStatus.Downloading or AutomaticUpdateStatus.Ready or AutomaticUpdateStatus.Queued or AutomaticUpdateStatus.Cancelling or AutomaticUpdateStatus.CheckingInstallation or AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.CancelFailed or AutomaticUpdateStatus.RetryWaiting or AutomaticUpdateStatus.Preparing);
    // A downloaded update waits while automatic apply is off; the user can apply it once.
    internal bool CanApply => !_blocked && !ApplyAfterExit && !_applyOnce && Status == AutomaticUpdateStatus.Ready && _ready != null && _handoff != null && _completion == null;
    internal void ApplyOnce() { if (!CanApply) return; _applyOnce = true; Revision++; }
    // The file replacement cannot stop once it starts; its result must still arrive as Committed or a failure.
    private bool Applying => _pending is { Queue: true };
    internal bool CanCancel => !_blocked && !Applying && Status is (AutomaticUpdateStatus.Downloading or AutomaticUpdateStatus.Preparing or AutomaticUpdateStatus.Ready or AutomaticUpdateStatus.Queued or AutomaticUpdateStatus.RetryWaiting);
    internal bool CanReactivate => _blocked && Fault != null && !_preferencesRepair && !_recovery && _failure == null && _reactivation == null && _cancel == null;
    internal AutomaticUpdates(UpdateDownloads downloads, IUpdateHandoff? handoff = null, IUpdateSafety? safety = null) { _downloads = downloads; _handoff = handoff; _safety = safety; }
    private void Set(AutomaticUpdateStatus status) {
        // After a commit the next launch runs the new version; later refreshes in this session must not
        // re-offer a switch or download against the old running image.
        if (Status == AutomaticUpdateStatus.Committed && status != AutomaticUpdateStatus.NotConfigured) return;
        if (Status != status) { Status = status; Revision++; }
        if (status is AutomaticUpdateStatus.Idle or AutomaticUpdateStatus.NotConfigured or AutomaticUpdateStatus.CheckingInstallation or AutomaticUpdateStatus.TransitionRequired or AutomaticUpdateStatus.MissingAssets) {
            _diagnosticAttempt=default;_diagnosticAttemptTarget=null;
        } else {
            if (_blocked && Fault!=null) CaptureDiagnosticOperation(Fault);
            if (_operation!=null) CaptureDiagnosticOperation(_operation);
        }
        DiagnosticHub.Status(DiagnosticFeature.Coordinator, SafeEnum.Parse<DiagnosticUpdateState>(status.ToString()),
            _diagnosticAttempt.Value!=null?_diagnosticAttemptTarget:_request?.Context.ReleaseTag,_diagnosticAttempt,authoritative:true);
    }
    private void CaptureDiagnosticOperation(UpdateFault? owner) {
        if(owner==null)return;
        _diagnosticAttempt=DiagnosticOperation.VerifiedGuid(owner.Operation);
        _diagnosticAttemptTarget=owner.Context.ReleaseTag;
    }
    private void CaptureQueuedDiagnostics(IUpdateHelperJob job) {
        // Read only already verified queue-owner memory; diagnostics cannot alter queue decisions.
        try {var next=job.DiagnosticContextOperation;
            if(next.Value!=null){if(_diagnosticAttempt.Value!=null && next.Value!=_diagnosticAttempt.Value)DiagnosticHub.Relate(_diagnosticAttempt,next);
                _diagnosticAttempt=next;_diagnosticAttemptTarget=DiagnosticIdentity.SafeVersion(job.DiagnosticTargetVersion);}
        }catch{}
    }
    private string Identity { get { if (_downloads.Feed is { RequiresAuthentication: false } feed) return feed.AnonymousIdentity;
        try { return _authentication?.CacheIdentity is { Length: > 0 and <= 256 } value ? value : ""; } catch { return ""; } } }
    private void Retire(bool clearRequest = false, bool fault = false) {
        var waits = new List<Task>(); if (_cancel != null) waits.Add(_cancel);
        if (_ready != null && _handoff != null) {
            var retired = _ready;
            try { if (fault) _handoff.SignalFailure(retired); else _handoff.SignalCancel(retired); waits.Add(Task.Run(() => _handoff.Cancel(retired))); }
            catch (Exception error) { waits.Add(Task.FromException(error)); }
        }
        _generation++; var pending = _pending; if (pending is { Queue: true }) pending = null; else _pending = null;
        if (pending != null) {
            try { pending.Cancellation.Cancel(); } catch { }
            waits.Add(pending.Task.ContinueWith(async task => {
                try { if (task.Status == TaskStatus.RanToCompletion) {
                    if (pending.Queue) ((IUpdateHelperJob)task.Result).Dispose();
                    else if (_handoff != null) await _handoff.Cancel(task.Result).ConfigureAwait(false);
                } else _ = task.Exception; } finally { pending.Cancellation.Dispose(); }
            }, TaskScheduler.Default).Unwrap());
        }
        var begin = _begin; _begin = null; var operation = _operation; CaptureDiagnosticOperation(operation); _operation = null;
        if (!fault && _safety != null && (begin != null || operation != null)) waits.Add(Task.Run(async () => {
            var owned = begin != null ? await begin.ConfigureAwait(false) : operation;
            await _safety.Complete(owned).ConfigureAwait(false);
        }));
        if (waits.Count != 0) {
            // Preserve the cancelled generation before logout/context replacement clears it.
            _retiredContext ??= _request?.Context ?? operation?.Context;
            _retiredStage ??= (_ready as IPreparedUpdate)?.Stage ?? RecoveryStage;
            _cancel = Task.WhenAll(waits);
        }
        _ready = null; _job?.Dispose(); _job = null; _downloads.Clear();
        if (clearRequest) { _request = null; _requestIdentity = ""; _autoSeen = ""; _attempts = 0; _explicitTransition = null; _applyOnce = false; _requiresTransition = false; }
    }
    internal void Configure(IReleaseAuthentication? authentication) {
        if (_configuredOnce && _downloads.Feed is { RequiresAuthentication: false }) { _authentication = authentication; return; }
        _configuredOnce = true;
        Retire(true); _authentication = authentication; _identity = Identity; _downloads.Configure(authentication);
        if (_blocked) return; RecoveryStage = null;
        _inspection = _safety != null ? Task.Run(() => _safety.Probe()) : Configured ? Task.Run(async () => {
            var stage = await _handoff!.Inspect().ConfigureAwait(false); return new UpdateSafetySnapshot(RecoveryRequired: stage != null, Stage: stage);
        }) : null;
        Set(_inspection != null ? AutomaticUpdateStatus.CheckingInstallation : Configured ? AutomaticUpdateStatus.Idle : AutomaticUpdateStatus.NotConfigured);
    }
    internal void Preferences(bool download, bool apply) {
        if (_blocked) {
            if (Fault != null) { if (download || apply) _preferencesRepair = true; }
            // Without a recorded fault (a FilesBusy wait, a recheck) the latest choice must hold when the block ends.
            else if (_failure == null && (AutoDownload != download || ApplyAfterExit != apply)) { AutoDownload = download; ApplyAfterExit = apply; Revision++; }
            return;
        }
        if (AutoDownload == download && ApplyAfterExit == apply) return;
        var stop = AutoDownload && !download && !Applying || ApplyAfterExit && !apply && Status == AutomaticUpdateStatus.Queued;
        AutoDownload = download; ApplyAfterExit = apply; Revision++;
        if (stop) { _explicitTransition = null; _applyOnce = false; Retire(); Set(Configured ? _cancel != null ? AutomaticUpdateStatus.Cancelling : AutomaticUpdateStatus.Cancelled : AutomaticUpdateStatus.NotConfigured); }
    }
    // A switched channel with no eligible target waits instead of asking for a review that cannot start.
    internal bool TransitionWaiting => Status == AutomaticUpdateStatus.TransitionRequired && _request == null && !_transitionCandidate;
    internal void Context(UpdateDownloadRequest? request, int sourceGeneration = 0, bool transitionCandidate = false) {
        if (Applying) return;
        if (_explicitTransition != null) { request = _explicitTransition; sourceGeneration = _transitionSourceGeneration; }
        _sourceGeneration = sourceGeneration;
        if (_transitionCandidate != transitionCandidate) { _transitionCandidate = transitionCandidate; Revision++; }
        var identity = request?.Identity ?? ""; if (_requestIdentity == identity) return;
        Retire(true); _request = request; _requestIdentity = identity;
        try { RefreshTransition(); } catch { Disable(FaultPhase.Preparation, FaultReason.Integrity, AutomaticUpdateStatus.RecoveryRequired); return; }
        if (_blocked) return;
        if (_cancel != null) { _contextAfterCancel = true; Set(AutomaticUpdateStatus.Cancelling); }
        else if (_inspection == null && RecoveryStage == null) Set(!Configured ? AutomaticUpdateStatus.NotConfigured : _requiresTransition ? AutomaticUpdateStatus.TransitionRequired : request == null ? AutomaticUpdateStatus.MissingAssets : AutomaticUpdateStatus.Idle);
    }
    private void RefreshTransition() {
        _requiresTransition = _request?.Context.Transition == null && _channels != null && _downloads.Feed is { } feed
            && _channels.ActiveChannel != feed.Channel;
    }
    internal (long Read, long Total) DownloadProgress => _downloads.PackageProgress;
    internal bool CanReviewTransition => Configured && !_blocked && Status != AutomaticUpdateStatus.Committed && _channels != null && _installedVersion == _channels.ActiveVersion && _downloads.Feed is { } feed
        && _channels.ActiveChannel != feed.Channel && _pending == null && _ready == null && _cancel == null
        && _inspection == null && _begin == null && _completion == null && _operation == null;
    internal Task<ChannelTransitionProposal> ProposeTransition(UpdateDownloadRequest request, DateTimeOffset now, CancellationToken cancellation)
        => CanReviewTransition ? _downloads.Propose(request, now, cancellation) : throw new InvalidOperationException("Channel transition is unavailable");
    internal bool CurrentTransitionProposal(ChannelTransitionProposal proposal) => CanReviewTransition && _downloads.CurrentProposal(proposal);
    // Separate explicit settings command. Existing saved OFF values remain OFF; only this
    // confirmed target may be queued once if the confirmation explicitly says after exit.
    internal bool ConfirmTransition(UpdateDownloadRequest request, int sourceGeneration, DateTimeOffset now, bool applyOnceAfterExit)
    {
        var feed = _downloads.Feed;
        if (!Configured || _blocked || request.Context.Transition == null || feed == null || _channels == null
            || _channels.ActiveChannel != request.Context.Transition.SourceChannel || _channels.ActiveChannel == request.Context.Channel
            || _channels.Root != request.Context.Transition.Root || _installedVersion != request.Context.CurrentVersion
            || request.Context.Repository != feed.Repository || request.Context.RepositoryId != feed.RepositoryId || request.Context.Channel != feed.Channel
            || _pending != null || _ready != null || _cancel != null || _inspection != null || _begin != null || _completion != null || _operation != null
            || Status is not (AutomaticUpdateStatus.Idle or AutomaticUpdateStatus.MissingAssets or AutomaticUpdateStatus.Cancelled or AutomaticUpdateStatus.TransitionRequired)) return false;
        _explicitTransition = null; Context(request, sourceGeneration);
        if (!CanDownload) return false;
        _explicitTransition = request; _transitionSourceGeneration = sourceGeneration; _applyOnce = applyOnceAfterExit;
        Download(now); return true;
    }
    internal void Download(DateTimeOffset now) {
        if (!CanDownload) return; _autoSeen = _requestIdentity; _attempts = 0;
        if (_safety == null) StartDownload(now);
        else { var context = _request!.Context; _begin = Task.Run(() => _safety.Begin(context)); Set(AutomaticUpdateStatus.Preparing); }
    }
    private void StartDownload(DateTimeOffset now) { _diagnosticAttempt=default;_diagnosticAttemptTarget=null;CaptureDiagnosticOperation(_operation);_attempts++; _downloads.Start(_request!, now, DiagnosticOperation.VerifiedGuid(_operation?.Operation)); Set(AutomaticUpdateStatus.Downloading); }
    internal void Cancel() { if (!CanCancel) return; _explicitTransition = null; _applyOnce = false; _contextAfterCancel = false; Retire(); Set(Configured ? _cancel != null ? AutomaticUpdateStatus.Cancelling : AutomaticUpdateStatus.Cancelled : AutomaticUpdateStatus.NotConfigured); }
    private void CompleteOperation() { var operation = _operation; CaptureDiagnosticOperation(operation); _operation = null; if (_safety != null && operation != null) _completion = Task.Run(() => _safety.Complete(operation)); }
    private void Disable(FaultPhase phase, FaultReason reason, AutomaticUpdateStatus status = AutomaticUpdateStatus.FaultDisabled, UpdateFault? known = null) {
        if (_blocked && _failure != null) return;
        var context = known?.Context ?? _retiredContext ?? _request?.Context;
        var operation = _operation; var stage = _retiredStage ?? RecoveryStage ?? (_ready as IPreparedUpdate)?.Stage;
        Retire(fault: true); _explicitTransition = null; _applyOnce = false; _blocked = true; AutoDownload = ApplyAfterExit = false; Revision++; Fault = known ?? Fault; Set(status);
        if (_safety == null || context == null) return;
        _failure = Task.Run(async () => {
            UpdateFault? fault = known; var failed = false;
            try { fault ??= await _safety.Fail(context, phase, reason, stage).ConfigureAwait(false); await _safety.Complete(operation).ConfigureAwait(false); }
            catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Staging, DiagnosticCode.StorageUnavailable, ex); failed = true; } // Keep operation guard if fault evidence cannot persist.
            try { await _safety.SavePreferences(false, false).ConfigureAwait(false); }
            catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Settings, DiagnosticPhase.Configuration, DiagnosticCode.StorageUnavailable, ex); failed = true; if (fault != null) { fault.PreferencesSaveFailed = true; try { await _safety.SaveFailed(fault).ConfigureAwait(false); } catch { } } }
            return new FaultOutcome(fault, failed);
        });
    }
    internal bool CanRecheck => _blocked && _failure == null && _cancel == null && _reactivation == null;
    internal void Recheck() {
        if (!CanRecheck || _safety == null) return;
        _blocked = false; _inspection = Task.Run(() => _safety.Probe()); Set(AutomaticUpdateStatus.CheckingInstallation);
    }
    internal void Reactivate() {
        if (!CanReactivate || _safety == null) return;
        _reactivation = Task.Run(async () => {
            var check = await _safety.Probe().ConfigureAwait(false);
            if (check.Busy || check.RecoveryRequired || check.Fault == null) throw new AutomaticUpdateException(AutomaticUpdateStatus.RecoveryRequired);
            try { await _safety.SavePreferences(true, true).ConfigureAwait(false); await _safety.Acknowledge().ConfigureAwait(false); }
            catch { try { await _safety.SavePreferences(false, false).ConfigureAwait(false); } catch { } throw; }
        }); Set(AutomaticUpdateStatus.Reactivating);
    }
    internal void Tick(DateTimeOffset now) {
        if (_disposed) return;
        if (_deferredChannel is { } channel && !Applying) SetChannel(channel);
        if (_failure is { IsCompleted: true } failure) { _failure = null; var outcome = failure.GetAwaiter().GetResult(); Fault = outcome.Fault; if (outcome.StorageFailed && !_recovery) Set(AutomaticUpdateStatus.FaultStorageFailed); else Set(Status); Revision++; }
        if (_preferencesRepair && _failure == null && Fault != null) { _preferencesRepair = false; Disable(Fault.Phase, Fault.Reason, Status, Fault); }
        if (_reactivation is { IsCompleted: true } activation) {
            _reactivation = null;
            try { activation.GetAwaiter().GetResult(); _blocked = _recovery = false; Fault = null; RecoveryStage = null; AutoDownload = ApplyAfterExit = true; _autoSeen = _requestIdentity; Set(Configured ? AutomaticUpdateStatus.Idle : AutomaticUpdateStatus.NotConfigured); Revision++; }
            catch (AutomaticUpdateException error) when (error.Status == AutomaticUpdateStatus.RecoveryRequired) { _recovery = true; Set(AutomaticUpdateStatus.RecoveryRequired); }
            catch (ApplySafetyException error) when (error.Failure == ApplyFailure.RecoveryRequired) { _recovery = true; Set(AutomaticUpdateStatus.RecoveryRequired); }
            catch { Set(_recovery ? AutomaticUpdateStatus.RecoveryRequired : AutomaticUpdateStatus.FaultStorageFailed); }
        }
        if (_inspection is { IsCompleted: true } inspection) {
            _inspection = null;
            try {
                var result = inspection.GetAwaiter().GetResult(); RecoveryStage = result.Stage; _recovery = result.RecoveryRequired; _channels = result.Channels; _installedVersion = result.InstalledVersion; RefreshTransition();
                if (result.Fault != null) Disable(result.Fault.Phase, result.Fault.Reason, _recovery ? AutomaticUpdateStatus.RecoveryRequired : AutomaticUpdateStatus.FaultDisabled, result.Fault);
                else if (result.Busy) { _blocked = true; _retryAt = now.AddSeconds(2); Set(AutomaticUpdateStatus.FilesBusy); }
                else if (_recovery) { _blocked = true; Set(AutomaticUpdateStatus.RecoveryRequired); }
                else Set(Configured ? _requiresTransition ? AutomaticUpdateStatus.TransitionRequired : AutomaticUpdateStatus.Idle : AutomaticUpdateStatus.NotConfigured);
            } catch (Exception ex) {
                DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.UpdateReadiness, DiagnosticCode.Unknown, ex);
                _blocked = _recovery = true; RecoveryStage = ""; AutoDownload = ApplyAfterExit = false; Set(AutomaticUpdateStatus.RecoveryRequired);
                if (_safety != null) _failure = Task.Run(async () => { try { await _safety.SavePreferences(false,false).ConfigureAwait(false); return new FaultOutcome(null,false); } catch { return new FaultOutcome(null,true); } });
            }
        }
        if (_cancel is { IsCompleted: true } cancel) {
            _cancel = null;
            try { cancel.GetAwaiter().GetResult(); _retiredContext = null; _retiredStage = null; if (!_blocked && Status == AutomaticUpdateStatus.Cancelling) Set(_contextAfterCancel && _request != null ? _requiresTransition ? AutomaticUpdateStatus.TransitionRequired : AutomaticUpdateStatus.Idle : AutomaticUpdateStatus.Cancelled); _contextAfterCancel = false; }
            catch { Disable(FaultPhase.Preparation, FaultReason.Storage, AutomaticUpdateStatus.CancelFailed); _retiredContext = null; _retiredStage = null; }
        }
        if (_completion is { IsCompleted: true } completion) { _completion = null; try { completion.GetAwaiter().GetResult(); } catch { Disable(FaultPhase.Preparation, FaultReason.Storage); } }
        if (_blocked && Status == AutomaticUpdateStatus.FilesBusy && _safety != null && _inspection == null && now >= _retryAt) {
            _blocked = false; _inspection = Task.Run(() => _safety.Probe()); Set(AutomaticUpdateStatus.CheckingInstallation);
        }
        if (_blocked) return;
        if (_identity != Identity && !Applying) { Retire(true); _identity = Identity; _downloads.Configure(_authentication); Set(Configured ? AutomaticUpdateStatus.Idle : AutomaticUpdateStatus.NotConfigured); return; }
        if (_begin is { IsCompleted: true } begin) { _begin = null; try { _operation = begin.GetAwaiter().GetResult(); StartDownload(now); } catch { Disable(FaultPhase.Preparation, FaultReason.Storage, AutomaticUpdateStatus.FaultStorageFailed); } }
        if (Status == AutomaticUpdateStatus.RetryWaiting && now >= _retryAt) StartDownload(now);
        _downloads.Poll();
        if (Status == AutomaticUpdateStatus.Downloading && _downloads.Result.Status != UpdateDownloadStatus.Downloading) {
            var result = _downloads.Result;
            if (result.Update is { } update && result.Status == UpdateDownloadStatus.Ready && _request != null && _handoff != null) {
                var cancellation = new CancellationTokenSource(); var context = _request.Context;
                _pending = new(Task.Run(() => _handoff.Prepare(update, context, cancellation.Token)), cancellation, _generation, false); Set(AutomaticUpdateStatus.Preparing);
            } else if (result.Status is UpdateDownloadStatus.Cancelled or UpdateDownloadStatus.AuthenticationRequired or UpdateDownloadStatus.AccessUnavailable or UpdateDownloadStatus.NotConfigured) {
                CompleteOperation(); Set(result.Status == UpdateDownloadStatus.Cancelled ? AutomaticUpdateStatus.Cancelled : AutomaticUpdateStatus.NotConfigured);
            } else {
                var transient = result.Status is UpdateDownloadStatus.Failed or UpdateDownloadStatus.TimedOut;
                var due = result.RetryAt is { } server && server > now.AddSeconds(60) ? server : now.AddSeconds(60);
                // GitHub's anonymous quota resets within the hour; waiting for it is the server's request, not a fault.
                if (result.Status == UpdateDownloadStatus.RateLimited && result.RetryAt != null) { _retryAt = due; Set(AutomaticUpdateStatus.RetryWaiting); }
                else if (transient && _attempts < 2 && due <= now.AddMinutes(15)) { _retryAt = due; Set(AutomaticUpdateStatus.RetryWaiting); }
                // A network-side failure changed nothing on disk: stop for this session only, without a latch or saved OFF
                // preferences, so the next launch (safe mode included, which has no Try Again) asks again.
                else if (transient || result.Status == UpdateDownloadStatus.RateLimited) { CompleteOperation(); Set(AutomaticUpdateStatus.Failed); }
                else Disable(result.Status is UpdateDownloadStatus.VerificationFailed or UpdateDownloadStatus.TooLarge ? FaultPhase.Verification : FaultPhase.Download,
                    result.Status == UpdateDownloadStatus.TimedOut ? FaultReason.Timeout : result.Status is UpdateDownloadStatus.VerificationFailed or UpdateDownloadStatus.TooLarge ? FaultReason.Integrity : FaultReason.Network);
            }
        }
        if (_pending is { } pending && pending.Task.IsCompleted) {
            _pending = null; pending.Cancellation.Dispose();
            try {
                var result = pending.Task.GetAwaiter().GetResult(); if (!pending.Queue && pending.Generation != _generation) return;
                if (pending.Queue) { _job = (IUpdateHelperJob)result; CaptureQueuedDiagnostics(_job); Set(AutomaticUpdateStatus.Queued); }
                else { _ready = result; CompleteOperation(); Set(AutomaticUpdateStatus.Ready); }
            } catch (AutomaticUpdateException error) {
                if (error.Status is AutomaticUpdateStatus.NotConfigured or AutomaticUpdateStatus.ManualUpgradeRequired or AutomaticUpdateStatus.FilesBusy or AutomaticUpdateStatus.AlreadyClaimed or AutomaticUpdateStatus.Cancelled) {
                    if (pending.Queue && error.Status != AutomaticUpdateStatus.NotConfigured) _ready = null;
                    CompleteOperation(); Set(error.Status);
                    // Antivirus or another program briefly holding a file: recheck and download again, at most three times.
                    if (error.Status == AutomaticUpdateStatus.FilesBusy) {
                        _blocked = true; _autoSeen = "";
                        _retryAt = ++_busyRetries <= 3 ? now.AddSeconds(30) : DateTimeOffset.MaxValue;
                    }
                }
                else { _recovery = error.Status == AutomaticUpdateStatus.RecoveryRequired; Disable(FaultPhase.Preparation, FaultReason.Preparation, _recovery ? error.Status : AutomaticUpdateStatus.FaultDisabled); }
            } catch (OperationCanceledException) { CompleteOperation(); Set(AutomaticUpdateStatus.Cancelled); }
            catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, pending.Queue ? DiagnosticPhase.HelperQueue : DiagnosticPhase.Staging, DiagnosticCode.Unknown, ex); Disable(FaultPhase.Preparation, FaultReason.Preparation); }
        }
        try {
            if (_job?.Poll() is { } completed) {
                if (completed is AutomaticUpdateStatus.RecoveryRequired or AutomaticUpdateStatus.Failed or AutomaticUpdateStatus.FaultDisabled) { RecoveryStage = (_ready as IPreparedUpdate)?.Stage; _recovery = completed == AutomaticUpdateStatus.RecoveryRequired; Disable(FaultPhase.Apply, FaultReason.Apply, _recovery ? completed : AutomaticUpdateStatus.FaultDisabled); }
                else { _job.Dispose(); _job = null; _ready = null; _explicitTransition = null; _applyOnce = false; Set(completed); }
            }
        } catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Coordinator, DiagnosticPhase.Apply, DiagnosticCode.InterruptedUnknown, ex); RecoveryStage = (_ready as IPreparedUpdate)?.Stage; _recovery = true; Disable(FaultPhase.Apply, FaultReason.Interrupted, AutomaticUpdateStatus.RecoveryRequired); }
        if (_blocked) return;
        if ((ApplyAfterExit || _applyOnce) && Status == AutomaticUpdateStatus.Ready && _completion == null && _ready != null && _handoff != null) {
            var ready = _ready; var cancellation = new CancellationTokenSource();
            _pending = new(Task.Run(() => _handoff.Queue(ready, cancellation.Token)), cancellation, _generation, true); Set(AutomaticUpdateStatus.Preparing);
        }
        if (AutoDownload && _requestIdentity.Length != 0 && _autoSeen != _requestIdentity && Status == AutomaticUpdateStatus.Idle) Download(now);
    }
    public void Dispose() { if (_disposed) return; Retire(true, _blocked); _disposed = true; _authentication = null; _downloads.Dispose(); Set(AutomaticUpdateStatus.NotConfigured); }
}

