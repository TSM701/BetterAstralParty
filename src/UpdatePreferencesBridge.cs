using BepInEx.Configuration;
namespace BetterAstralParty;

// Shared production/fixture event path. Suppress only this writer's expected synchronous events.
internal sealed class UpdatePreferencesBridge : IDisposable
{
    private readonly ConfigFile _config;
    private readonly ConfigEntry<bool> _download, _apply;
    private readonly object _gate = new();
    private int _changed, _writer;
    private bool _expectedDownload, _expectedApply;
    internal UpdatePreferencesBridge(ConfigFile config, ConfigEntry<bool> download, ConfigEntry<bool> apply)
    {
        _config=config; _download=download; _apply=apply;
        _download.SettingChanged+=DownloadChanged; _apply.SettingChanged+=ApplyChanged;
    }
    private void DownloadChanged(object? sender, EventArgs args) => Changed(_download.Value, _expectedDownload);
    private void ApplyChanged(object? sender, EventArgs args) => Changed(_apply.Value, _expectedApply);
    private void Changed(bool value, bool expected)
    {
        if (Volatile.Read(ref _writer)==Environment.CurrentManagedThreadId && value==expected) return;
        Interlocked.Exchange(ref _changed,1);
    }
    internal void Poll(AutomaticUpdates model)
    {
        if (Volatile.Read(ref _writer)!=0 || model.Status==AutomaticUpdateStatus.Reactivating) return;
        if (Interlocked.Exchange(ref _changed,0)!=0) model.Preferences(_download.Value,_apply.Value);
    }
    internal Task Save(bool download, bool apply)
    {
        lock(_gate) {
            var previous=_config.SaveOnConfigSet; _expectedDownload=download; _expectedApply=apply;
            Volatile.Write(ref _writer,Environment.CurrentManagedThreadId);
            try { _config.SaveOnConfigSet=false; _download.Value=download; _apply.Value=apply; _config.Save(); }
            finally { _config.SaveOnConfigSet=previous; Volatile.Write(ref _writer,0); }
        }
        return Task.CompletedTask;
    }
    public void Dispose() { _download.SettingChanged-=DownloadChanged; _apply.SettingChanged-=ApplyChanged; }
}
