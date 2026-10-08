using BetterAstralParty.Observability;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using BetterAstralParty.Updating;

namespace BetterAstralParty;

// Private credential input is approved. Persistence uses the Windows vault, never game settings.
internal static class PrivateUpdateApproval
{
    internal static bool CredentialInputEnabled => true;
}
internal enum ReleaseSessionStatus { NotConfigured, SignedOut, AwaitingInput, Connected, InvalidInput, Cancelled, Expired, AuthenticationRequired, AccessUnavailable, InputFailed, Restoring, StorageFailed }
internal interface IReleaseCredentialInput
{
    Task<ReleaseCredential?> Read(bool korean, CancellationToken cancellation);
}
internal sealed class ReleaseInputRejectedException : Exception { internal ReleaseInputRejectedException() : base("Private input rejected") { } }
// Deliberately not a record: generated ToString must never expose the credential.
internal sealed class ReleaseCredential(string token) : IDisposable
{
    private string? _token = token;
    internal bool IsEmpty => Volatile.Read(ref _token) == null;
    internal string? Take() => Interlocked.Exchange(ref _token, null);
    public void Dispose() => Interlocked.Exchange(ref _token, null);
    public override string ToString() => "ReleaseCredential";
}
// Scoped, memory-only bearer provider. It does not read cfg, environment, OS credentials or disk.
internal sealed class MemoryReleaseAuthentication : IReleaseAuthentication, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<TimeSpan> _elapsed;
    private readonly TimeSpan _started;
    private readonly TimeSpan? _lifetime;
    private string? _token;
    private string _identity = Guid.NewGuid().ToString("N");
    internal MemoryReleaseAuthentication(string token, TimeSpan? lifetime = null, Func<TimeSpan>? elapsed = null)
    {
        if (!ValidToken(token) || lifetime is { } limit && (limit <= TimeSpan.Zero || limit > TimeSpan.FromHours(8))) throw new ArgumentException("Invalid private session");
        _elapsed = elapsed ?? MonotonicClock(); _started = _elapsed(); _lifetime = lifetime; _token = token;
    }
    private static Func<TimeSpan> MonotonicClock() { var watch = Stopwatch.StartNew(); return () => watch.Elapsed; }
    internal static bool ValidToken(string? token) => token is { Length: <= 256 }
        && Regex.IsMatch(token, @"\Agithub_pat_[A-Za-z0-9_]{20,245}\z", RegexOptions.CultureInvariant);
    public string CacheIdentity { get { lock (_gate) { Expire(); return _identity; } } }
    private void Expire() { if (_lifetime is not { } limit) return; var age = _elapsed() - _started; if (age < TimeSpan.Zero || age >= limit) { _token = null; _identity = ""; } }
    internal static bool Allowed(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (request.Method != HttpMethod.Get || uri == null || !uri.IsAbsoluteUri || uri.Scheme != "https"
            || uri.Host != "api.github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || request.Headers.Authorization != null || uri.OriginalString != "https://api.github.com" + uri.AbsolutePath + uri.Query) return false;
        if (uri.AbsolutePath == "/repos/TSM701/BetterAstralPartyBeta" && uri.Query.Length == 0) return true;
        const string prefix = "/repos/TSM701/BetterAstralPartyBeta/releases";
        if (uri.AbsolutePath == prefix)
            return uri.Query == "?per_page=100" || Regex.IsMatch(uri.Query,
                @"\A\?(?:per_page=100&page=[1-9][0-9]{0,5}|page=[1-9][0-9]{0,5}&per_page=100)\z", RegexOptions.CultureInvariant);
        return uri.Query.Length == 0 && Regex.IsMatch(uri.AbsolutePath,
            @"\A/repos/TSM701/BetterAstralPartyBeta/releases/assets/[1-9][0-9]{0,18}\z", RegexOptions.CultureInvariant);
    }
    public bool TryAuthorize(HttpRequestMessage request)
    {
        lock (_gate)
        {
            Expire();
            if (_token == null || _identity.Length == 0 || !Allowed(request)) return false;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return true;
        }
    }
    public void Dispose() { lock (_gate) { _token = null; _identity = ""; } }
    public override string ToString() => "MemoryReleaseAuthentication";
}
// Main-thread coordinator; input and network run on workers. Logout retires all three pipelines.
internal sealed class PrivateReleaseSession : IDisposable
{
    private sealed record Pending(Task<ReleaseCredential?> Task, CancellationTokenSource Cancellation, bool Restoring = false);
    private readonly ReleaseUpdates _updates;
    private readonly AutomaticUpdates _automatic;
    private readonly Action<IReleaseAuthentication?> _configureUpdates;
    private readonly Func<bool> _allowed;
    private readonly Func<TimeSpan>? _elapsed;
    private readonly IReleaseCredentialStore? _store;
    private readonly object _gate = new(); // Serialize ProcessExit against main-thread publication.
    private MemoryReleaseAuthentication? _authentication;
    private Pending? _pending;
    private bool _disposed;
    private int _promptRevision = WindowsReleaseCredentialInput.PromptRevision;
    internal ReleaseSessionStatus Status { get; private set; }
    internal int Revision { get; private set; }
    internal bool CanConnect { get { lock (_gate) return !_disposed && _allowed() && _pending == null && !WindowsReleaseCredentialInput.PromptInProgress; } }
    internal bool CanSignOut { get { lock (_gate) return !_disposed && (_authentication != null || _pending != null || _store != null); } }
    internal PrivateReleaseSession(ReleaseUpdates updates, AutomaticUpdates automatic, Func<bool> allowed, Func<TimeSpan>? elapsed = null,
        Action<IReleaseAuthentication?>? configureUpdates = null, IReleaseCredentialStore? store = null)
    {
        _updates = updates; _automatic = automatic; _allowed = allowed; _elapsed = elapsed; _store = store;
        _configureUpdates = configureUpdates ?? updates.ConfigureAuthentication;
        Status = allowed() ? ReleaseSessionStatus.SignedOut : ReleaseSessionStatus.NotConfigured;
        if (allowed() && store != null)
        {
            var cancellation = new CancellationTokenSource();
            _pending = new(Task.Run(() => {
                cancellation.Token.ThrowIfCancellationRequested();
                var token = store.Load();
                return token == null ? null : new ReleaseCredential(token);
            }), cancellation, Restoring: true);
            Set(ReleaseSessionStatus.Restoring);
        }
    }
    internal static PrivateReleaseSession Production(ReleaseUpdates updates, AutomaticUpdates automatic) =>
        new(updates, automatic, () => PrivateUpdateApproval.CredentialInputEnabled && UpdateTrust.Production().Configured,
            store: new WindowsReleaseCredentialStore());
    private void Set(ReleaseSessionStatus status) { if (Status != status) { Status = status; Revision++; DiagnosticHub.Status(DiagnosticFeature.Authentication, SafeEnum.Parse<DiagnosticUpdateState>(status.ToString())); } }
    internal void Connect(IReleaseCredentialInput input, bool korean)
    { lock (_gate) ConnectCore(input, korean); }
    private void ConnectCore(IReleaseCredentialInput input, bool korean)
    {
        if (_disposed) return;
        if (!_allowed()) { Retire(ReleaseSessionStatus.NotConfigured); return; }
        if (_pending != null || WindowsReleaseCredentialInput.PromptInProgress) return;
        Retire(ReleaseSessionStatus.SignedOut);
        var cancellation = new CancellationTokenSource();
        _pending = new(Task.Run(() => input.Read(korean, cancellation.Token)), cancellation);
        Set(ReleaseSessionStatus.AwaitingInput);
    }
    private void Retire(ReleaseSessionStatus status)
    {
        _authentication?.Dispose(); _authentication = null;
        var pending = _pending; _pending = null;
        if (pending != null)
        {
            try { pending.Cancellation.Cancel(); } catch { }
            _ = pending.Task.ContinueWith(task => {
                try { if (task.Status == TaskStatus.RanToCompletion) task.Result?.Dispose(); else _ = task.Exception; }
                finally { pending.Cancellation.Dispose(); }
            }, TaskScheduler.Default);
        }
        _configureUpdates(null); _automatic.Configure(null); Set(status);
    }
    private void DeleteSaved(ReleaseSessionStatus status)
    {
        Retire(status);
        try { _store?.Delete(); }
        catch { Set(ReleaseSessionStatus.StorageFailed); }
    }
    internal void SignOut() { lock (_gate) { if (!_disposed) DeleteSaved(_allowed() ? ReleaseSessionStatus.SignedOut : ReleaseSessionStatus.NotConfigured); } }
    internal void Poll()
    { lock (_gate) PollCore(); }
    private void PollCore()
    {
        if (_disposed) return;
        var promptRevision = WindowsReleaseCredentialInput.PromptRevision;
        if (_promptRevision != promptRevision) { _promptRevision = promptRevision; Revision++; }
        if (!_allowed()) { if (_authentication != null || _pending != null) Retire(ReleaseSessionStatus.NotConfigured); else Set(ReleaseSessionStatus.NotConfigured); return; }
        if (_authentication != null && _authentication.CacheIdentity.Length == 0) { Retire(ReleaseSessionStatus.Expired); return; }
        if (_authentication != null && _updates.Channel == ReleaseChannel.Beta && (_updates.Result.Status is ReleaseUpdateStatus.AuthenticationRequired or ReleaseUpdateStatus.AccessUnavailable
            || _updates.Result.Failure is ReleaseUpdateStatus.AuthenticationRequired or ReleaseUpdateStatus.AccessUnavailable || _automatic.AuthenticationLost))
        {
            var denied = _updates.Result.Status == ReleaseUpdateStatus.AccessUnavailable || _updates.Result.Failure == ReleaseUpdateStatus.AccessUnavailable || _automatic.AccessLost;
            if (denied) Retire(ReleaseSessionStatus.AccessUnavailable);
            else DeleteSaved(ReleaseSessionStatus.AuthenticationRequired);
            return;
        }
        if (_pending is not { } pending || !pending.Task.IsCompleted) return;
        _pending = null;
        try
        {
            using var credential = pending.Task.GetAwaiter().GetResult();
            if (pending.Cancellation.IsCancellationRequested || credential == null) { Set(pending.Restoring ? ReleaseSessionStatus.SignedOut : ReleaseSessionStatus.Cancelled); return; }
            var token = credential.Take();
            if (!MemoryReleaseAuthentication.ValidToken(token)) { Set(ReleaseSessionStatus.InvalidInput); return; }
            if (!pending.Restoring && _store != null)
            {
                try { _store.Save(token!); }
                catch { Retire(ReleaseSessionStatus.StorageFailed); return; }
            }
            _authentication = new MemoryReleaseAuthentication(token!, elapsed: _elapsed);
            _configureUpdates(_authentication); _automatic.Configure(_authentication);
            Set(ReleaseSessionStatus.Connected); // Session input accepted, not a claim of server authentication.
        }
        catch (ReleaseInputRejectedException) { Retire(ReleaseSessionStatus.InvalidInput); }
        catch (OperationCanceledException) { Retire(ReleaseSessionStatus.Cancelled); }
        catch (Exception ex) { DiagnosticHub.Failure(DiagnosticFeature.Authentication, DiagnosticPhase.AuthenticationInput, DiagnosticCode.Unknown, ex); Retire(pending.Restoring ? ReleaseSessionStatus.StorageFailed : ReleaseSessionStatus.InputFailed); }
        finally { pending.Cancellation.Dispose(); }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; Retire(ReleaseSessionStatus.SignedOut); } }
    // Natural game exit must leave an already queued, verified helper free to apply offline.
    // Explicit sign-out/Dispose still retires the handoff; process exit only forgets secrets/input.
    internal void ProcessExiting()
    { lock (_gate) ProcessExitingCore(); }
    private void ProcessExitingCore()
    {
        if (_disposed) return;
        _disposed = true; // Irreversible before retiring secrets; no later authentication publication.
        _authentication?.Dispose(); _authentication = null;
        var pending = _pending; _pending = null;
        if (pending != null)
        {
            try { pending.Cancellation.Cancel(); } catch { }
            _ = pending.Task.ContinueWith(task => {
                try { if (task.Status == TaskStatus.RanToCompletion) task.Result?.Dispose(); else _ = task.Exception; }
                finally { pending.Cancellation.Dispose(); }
            }, TaskScheduler.Default);
        }
        Set(ReleaseSessionStatus.SignedOut);
    }
}
