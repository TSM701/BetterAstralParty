#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterAstralParty.Updating {
    internal sealed class DetachedSignature {
        internal readonly string KeyId; internal readonly byte[] Signature;
        internal DetachedSignature(string key, byte[] signature) { KeyId=key; Signature=(byte[])signature.Clone(); }
        internal byte[] Bytes() { return Encoding.ASCII.GetBytes("BetterAstralParty.UpdateSignature/v1\nkey-id="+KeyId+"\nsignature="+Convert.ToBase64String(Signature)+"\n"); }
        internal static DetachedSignature Parse(byte[] bytes) {
            if(bytes.Length==0 || bytes.Length>1024) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            foreach(var value in bytes) if(value>127 || value==13) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            var lines=Encoding.ASCII.GetString(bytes).Split('\n');
            if(lines.Length!=4 || lines[0]!="BetterAstralParty.UpdateSignature/v1" || !lines[1].StartsWith("key-id=",StringComparison.Ordinal) || !lines[2].StartsWith("signature=",StringComparison.Ordinal) || lines[3]!="") throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            var key=lines[1].Substring(7); if(!Regex.IsMatch(key,@"\A[A-Za-z0-9_-]{1,40}\z")) throw new UpdateValidationException(UpdateFailure.InvalidSignature);
            try { var result=new DetachedSignature(key,Convert.FromBase64String(lines[2].Substring(10)));
                if(result.Signature.Length==0 || result.Signature.Length>UpdateTrust.MaxSignatureBytes || Encoding.ASCII.GetString(result.Bytes())!=Encoding.ASCII.GetString(bytes)) throw new FormatException();
                return result;
            } catch(FormatException) { throw new UpdateValidationException(UpdateFailure.InvalidSignature); }
        }
    }
    // 1.3.x (protocol 4) receipt: read only to take over its exact owned files.
    internal sealed class LegacyReceipt {
        internal const string Baseline = "1.3.0";
        internal static readonly string[] Owned = { "AstralParty-Vanilla.cmd", UpdateTrust.PluginPath, OrdinaryInstallRecovery.LegacyLauncher, "BetterAstralParty-Mod.cmd", "BetterAstralParty.compatibility.json" };
        internal readonly bool? LoaderBefore; internal readonly Dictionary<string,string> Files;
        private LegacyReceipt(bool? loader,Dictionary<string,string> files) {LoaderBefore=loader;Files=files;}
        private static ApplySafetyException Bad() { return new ApplySafetyException(ApplyFailure.InvalidState); }
        internal static bool Is(byte[] bytes) { try { Parse(bytes); return true; } catch(ApplySafetyException) { return false; } }
        internal static LegacyReceipt Parse(byte[] bytes) {
            try {
                var obj=new InstallReceipt.Json(bytes,maxText:2048).Parse() as Dictionary<string,object?>;
                if(obj==null || obj.Count!=6 || !Equals(obj.ContainsKey("baseline")?obj["baseline"]:null,Baseline) || !obj.ContainsKey("schema") || !Equals(obj["schema"],1)
                    || !Equals(obj.ContainsKey("owner")?obj["owner"]:null,InstalledPluginMetadata.Owner) || !obj.ContainsKey("loaderBefore") || !obj.ContainsKey("files") || !obj.ContainsKey("channelState")) throw Bad();
                if(obj["loaderBefore"]!=null && !(obj["loaderBefore"] is bool)) throw Bad();
                var array=obj["files"] as List<object?>; if(array==null || array.Count!=Owned.Length) throw Bad();
                var files=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var entry in array) { var item=entry as Dictionary<string,object?>;
                    if(item==null || item.Count!=2 || !(item.ContainsKey("path") && item["path"] is string) || !(item.ContainsKey("sha256") && item["sha256"] is string)) throw Bad();
                    var path=(string)item["path"]!; var hash=(string)item["sha256"]!;
                    if(Array.IndexOf(Owned,path)<0 || !Regex.IsMatch(hash,@"\A[0-9A-F]{64}\z") || files.ContainsKey(path)) throw Bad(); files.Add(path,hash);
                }
                return new LegacyReceipt((bool?)obj["loaderBefore"],files);
            } catch(System.Text.DecoderFallbackException) { throw Bad(); }
        }
    }
    // Explicit approved-whole installation: the package carries a signed descriptor over its exact
    // owned payload; the receipt and high-water are derived only from that verified proof.
    internal static class InstallProof {
        private static ApplySafetyException Bad(string? reason=null) {
            var error=new ApplySafetyException(ApplyFailure.RecoveryRequired);
            if(reason!=null)error.Data["BapRecoveryReason"]=reason;
            return error;
        }
        private static WindowsFileFence.FileLease? Maybe(WindowsFileFence f, string p) {
            try { return f.OpenFile(p); } catch (ApplySafetyException e) when(e.NativeError == 2) { return null; }
        }
        internal static VerifiedUpdateDescriptor VerifyUpdateProof(byte[] bytes,byte[] signature) {
            var envelope=DetachedSignature.Parse(signature);
            var l=Encoding.ASCII.GetString(bytes).Split('\n');
            var local=l.Length>0 && l[0]==UpdateTrust.LocalDescriptorHeader;
            if(l.Length<15 || !local && l[0]!=UpdateTrust.DescriptorHeader || !l[1].StartsWith("repository=",StringComparison.Ordinal) || !l[2].StartsWith("version=",StringComparison.Ordinal) || !l[3].StartsWith("channel=",StringComparison.Ordinal) || !l[5].StartsWith("release-id=",StringComparison.Ordinal) || !l[6].StartsWith("asset-id=",StringComparison.Ordinal) || !l[12].StartsWith("repository-id=",StringComparison.Ordinal)) throw Bad();
            long release,asset,id;
            if(!long.TryParse(l[5].Substring(11),NumberStyles.None,CultureInfo.InvariantCulture,out release) || !long.TryParse(l[6].Substring(9),NumberStyles.None,CultureInfo.InvariantCulture,out asset) || !long.TryParse(l[12].Substring(14),NumberStyles.None,CultureInfo.InvariantCulture,out id)) throw Bad();
            if(local && (release!=0 || asset!=0)) throw Bad();
            var channel=l[3].Substring(8);
            if(!local && l[11]=="purpose="+ReleaseFeedPolicy.PublicPurpose && UpdateVersion.Parse(l[2].Substring(8)).IsPrerelease)channel="PreRelease";
            var context=local ? UpdateContext.LocalInstall(l[1].Substring(11),channel,l[2].Substring(8),id)
                : new UpdateContext(l[1].Substring(11),channel,"0.0.0",l[2].Substring(8),release,asset,UpdateTrust.PlatformId,repositoryId:id);
            var trust=UpdateTrust.Production();
            return local ? trust.VerifyLocalInstall(bytes,envelope.KeyId,envelope.Signature,context)
                : trust.VerifyProposal(bytes,envelope.KeyId,envelope.Signature,context);
        }
        // Package file for each owned game-root path.
        internal static string PackagePath(string owned) {
            return owned==UpdateTrust.PluginPath ? "artifacts/BetterAstralParty.dll" : owned=="BetterAstralParty.compatibility.json" ? "artifacts/"+owned : "tools/"+owned;
        }
        internal sealed class Inputs : IDisposable {
            internal readonly WindowsFileFence Source;
            internal readonly VerifiedUpdateDescriptor Update;
            internal readonly string ManifestHash;
            private readonly List<IDisposable> _leases=new List<IDisposable>();
            private byte[] Read(string p, long limit) { var f=Source.OpenLaunchImage(p);_leases.Add(f);return UpdateTicket.Read(f,limit); }
            internal Inputs(string root,string package,string version) {
                Source=new WindowsFileFence(package);
                try {
                    var actual=WindowsPath.Absolute(root).TrimEnd('\\');
                    if(actual.Equals(Source.Root,StringComparison.OrdinalIgnoreCase) || actual.StartsWith(Source.Root+"\\",StringComparison.OrdinalIgnoreCase) || Source.Root.StartsWith(actual+"\\",StringComparison.OrdinalIgnoreCase)) throw Bad("PackageInsideGame");
                    ManifestHash=UpdateTrust.Hash(Read("package-manifest.json",1024*1024));
                    var descriptor=Read("update-proof/update.descriptor",UpdateTrust.MaxDescriptorBytes);
                    var signature=Read("update-proof/update.signature",1024);
                    Update=VerifyUpdateProof(descriptor,signature);
                    if(Update.Version!=version || Update.Protocol!=UpdateTrust.Protocol || Update.Files.Count!=InstallReceipt.Owned.Length) throw Bad("PackageProofMismatch");
                    foreach(var file in Update.Files) {
                        var payload=Read(PackagePath(file.Path),UpdateTrust.MaxFileBytes);
                        if(payload.LongLength!=file.Bytes || UpdateTrust.Hash(payload)!=file.Sha256) throw Bad("PackagePayloadMismatch");
                        if(file.Path==UpdateTrust.PluginPath) {
                            var metadata=InstalledPluginMetadata.Read(payload);
                            if(metadata.Version!=version || metadata.UpdateProtocol!=UpdateTrust.Protocol || metadata.SettingsSchema!=1)throw Bad("PackagePluginMismatch");
                        }
                    }
                } catch {Dispose();throw;}
            }
            public void Dispose() {for(var i=_leases.Count-1;i>=0;i--)_leases[i].Dispose();_leases.Clear();Source.Dispose();}
        }
        internal static void ValidateInputs(string root,string package,string version) { using(var input=new Inputs(root,package,version)) { } }
        // Long-path preflight for every path maintenance may create under the game and journal roots.
        internal static void ValidateMaintenancePaths(string root,string journalRoot) {
            WindowsFileFence.ValidatePlannedPath(root);WindowsFileFence.ValidatePlannedPath(journalRoot);
            var id=new string('a',32);
            foreach(var path in InstallReceipt.Owned)WindowsFileFence.ValidatePlannedPath(System.IO.Path.Combine(root,path.Replace('/','\\')));
            foreach(var path in new[]{InstallReceipt.Path,UpdateTransaction.HighWaterPath,UpdateFaults.OperationPath,UpdateFaults.FailurePath,OrdinaryInstallRecovery.LegacyLauncher,OrdinaryInstallRecovery.LegacyHelper,OrdinaryInstallRecovery.LegacyHelperReceipt})
                WindowsFileFence.ValidatePlannedPath(System.IO.Path.Combine(root,path));
            foreach(var kind in new[]{"txn","stage"})
                foreach(var leaf in new[]{"plan","committed","commit-intent","rolled-back","settings.backup","descriptor.bin","signature.bin","key-id.bin","package.zip","transition.bin","channel.bin","old-05","new-05"})
                    WindowsFileFence.ValidatePlannedPath(System.IO.Path.Combine(root,UpdateWorkPath.At(kind,id).Replace('/','\\'),leaf));
            foreach(var path in new[]{"pending.json","pending.json.lock",id+"/pending.json",id+"/completed.json",id+"/restored.json",id+"/299.bin"})
                WindowsFileFence.ValidatePlannedPath(System.IO.Path.Combine(journalRoot,path.Replace('/','\\')));
        }
        // Structural identity checks, then the version decision of InstallPolicy.Whole.
        internal static void ValidateChannel(ReceiptChannelState? state,string root,string currentVersion,int currentProtocol,int currentSettings,VerifiedUpdateDescriptor incoming,string? high,string? highDescriptor=null) {
            if(state==null)throw Bad("ChannelStateMissing");
            if(state.Root!=root || state.ActiveVersion!=currentVersion || state.ActiveRepositoryId!=ReleaseFeedPolicy.For(state.ActiveChannel).RepositoryId)throw Bad("ChannelStateMismatch");
            var feed=ReleaseFeedPolicy.For(incoming.Channel);
            if(currentProtocol!=UpdateTrust.Protocol || currentSettings!=1 || incoming.Protocol!=UpdateTrust.Protocol || incoming.SettingsSchema!=1 || incoming.Files.Count!=InstallReceipt.Owned.Length
                || incoming.Repository!=feed.Repository || incoming.RepositoryId!=feed.RepositoryId
                || !(incoming.ReleaseId>0 && incoming.AssetId>0 || incoming.ReleaseId==0 && incoming.AssetId==0))throw Bad("ChannelProofMismatch");
            var refusal=InstallPolicy.Whole(state,currentVersion,incoming,high,highDescriptor);
            if(refusal!=InstallPolicy.Refusal.None)throw new UpdateValidationException(UpdateFailure.NotNewer,InstallPolicy.Explain(refusal,state,currentVersion,incoming,high));
        }
        // Receipt bytes for the package: preserve existing protocol-5 channel history, or start a new one.
        internal static string PrepareReceipt(string root,string package,string version,string loaderBefore) {
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root)) {
                using(var file=Maybe(f,InstallReceipt.Path)) {
                    byte[]? bytes=file==null?null:UpdateTicket.Read(file,16384);
                    if(bytes!=null && !LegacyReceipt.Is(bytes)) {
                        var old=InstallReceipt.Parse(bytes);
                        using var history=UpdateTransaction.ValidateHistory(f,old);
                        using(var plugin=Maybe(f,UpdateTrust.PluginPath)) {
                            if(plugin==null)throw Bad("InstalledPluginMissing");
                            var current=InstalledPluginMetadata.Read(UpdateTicket.Read(plugin,UpdateTrust.MaxFileBytes));
                            ValidateChannel(old.Channels,f.RootIdentity.Text,current.Version,current.UpdateProtocol,current.SettingsSchema,input.Update,UpdateTransaction.ReadHighWater(f),UpdateTransaction.HighWaterDescriptor(f));
                        }
                        var target=InstallReceipt.Parse(old.Updated(input.Update));
                        return Encoding.ASCII.GetString(target.InitializeChannel(f.RootIdentity.Text,input.Update));
                    }
                    if(loaderBefore!="true" && loaderBefore!="false" && loaderBefore!="null")throw Bad();
                    return Encoding.ASCII.GetString(InstallReceipt.Create(f.RootIdentity.Text,input.Update,loaderBefore=="null"?(bool?)null:loaderBefore=="true"));
                }
            }
        }
        // High-water after installing this package; null keeps a higher existing record unchanged.
        internal static byte[]? HighWater(string root,string package,string version) {
            using(var input=new Inputs(root,package,version))using(var f=new WindowsFileFence(root))
                return UpdateTransaction.NextWater(f.RootIdentity.Text,UpdateTransaction.ReadHighWater(f),input.Update);
        }
        internal static string InstalledVersionState(string root) {
            using(var f=new WindowsFileFence(root)) {
                string? hash;var high=UpdateTransaction.ReadHighWater(f,out hash);
                return "BetterAstralParty.InstallerState/v1\nroot="+f.RootIdentity.Text+"\nversion="+(high??"")+"\nwater-sha="+(hash??"")+"\n";
            }
        }
    }
}
