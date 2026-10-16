#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Packaging
{
    // Offline packaging only: public pins and canonical signing inputs; never signs or accesses credentials.
    public static class GaPackageContract
    {
        public static string[] OwnedPaths() { return (string[])InstallReceipt.Owned.Clone(); }
        public static string Platform { get { return UpdateTrust.PlatformId; } }
        public static string[] PublicKeyIds() {
            var keys = ProductionUpdateKeys.Read(); var ids = new string[keys.Length];
            for (var i=0;i<keys.Length;i++) ids[i]=keys[i].Id;
            return ids;
        }
        public static byte[] PrepareHelper(string version, byte[] image) {
            if (UpdateVersion.Parse(version).Tag != version) throw new ArgumentException("Canonical version required");
            return HelperDescriptor.Prepare(version, image, 4);
        }
        public static string VerifyHelper(string version, byte[] descriptor, byte[] signature, byte[] image) {
            return HelperDescriptor.Verify(UpdateTrust.Production(), descriptor, DetachedSignature.Parse(signature), image, version).Sha256;
        }
        public static string VerifyHelperBootstrap(byte[] descriptor,byte[] signature,byte[] image) {
            var helper=HelperDescriptor.Verify(UpdateTrust.Production(),descriptor,DetachedSignature.Parse(signature),image);
            if(helper.Protocol!=4)throw new ArgumentException("Protocol4 helper required");
            return helper.Sha256;
        }
        public static byte[] PreviewUpdateZip(byte[][] payload) {
            if(payload.Length!=InstallReceipt.Owned.Length)throw new ArgumentException("Exact native owned inventory required");
            var entries=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
            for(var i=0;i<payload.Length;i++){if(payload[i].Length==0 || payload[i].LongLength>UpdateTrust.MaxFileBytes)throw new ArgumentException("Invalid owned payload");entries.Add(InstallReceipt.Owned[i],payload[i]);}
            using(var stream=new MemoryStream()){
                using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true))foreach(var pair in entries){
                    var entry=archive.CreateEntry(pair.Key,CompressionLevel.Optimal);entry.LastWriteTime=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
                    using(var output=entry.Open())output.Write(pair.Value,0,pair.Value.Length);
                }
                var zip=stream.ToArray();if(zip.LongLength>UpdateTrust.MaxZipBytes)throw new ArgumentException("Update ZIP limit");return zip;
            }
        }
        public static string VerifyWholeUpdate(string version,byte[] descriptor,byte[] signature,byte[] zip) {
            var verified=WholeInstallBridge.VerifyUpdateProof(descriptor,signature);
            if(verified.Protocol!=4 || verified.Version!=version || verified.Files.Count!=InstallReceipt.Owned.Length)throw new ArgumentException("Exact native-generation proof required");
            UpdatePackage.VerifyPayload(zip,verified);return verified.DescriptorSha256;
        }
        public static string VerifyHistoricalUpdate(byte[] descriptor,byte[] signature) {
            var proof=WholeInstallBridge.VerifyUpdateProof(descriptor,signature);
            if(proof.Protocol!=4 || proof.SettingsSchema!=1 || proof.Files.Count!=InstallReceipt.Owned.Length)throw new ArgumentException("Signed native-generation history required");
            return proof.DescriptorSha256;
        }
        public static byte[] PrepareUpdateDescriptor(string version, string channel, long releaseId, long assetId, byte[] zip, long[] sizes, string[] hashes) {
            return PrepareDescriptor(version,channel,releaseId,assetId,zip,sizes,hashes,false);
        }
        public static byte[] PrepareLocalInstallDescriptor(string version, string channel, byte[] zip, long[] sizes, string[] hashes) {
            return PrepareDescriptor(version,channel,0,0,zip,sizes,hashes,true);
        }
        private static byte[] PrepareDescriptor(string version, string channel, long releaseId, long assetId, byte[] zip, long[] sizes, string[] hashes, bool localInstall) {
            var v=UpdateVersion.Parse(version);
            if(v.Tag!=version || v.CompareTo(UpdateVersion.Parse("1.3.0-0"))<0 || !ReleaseFeedPolicy.IsChannel(channel) || !localInstall && v.IsLocalPrerelease || channel=="Stable" && v.IsPrerelease && !(localInstall && v.IsLocalPrerelease)
                || (localInstall ? releaseId!=0 || assetId!=0 : releaseId<=0 || assetId<=0) || zip.Length==0 || zip.LongLength>UpdateTrust.MaxZipBytes
                || sizes.Length!=InstallReceipt.Owned.Length || hashes.Length!=sizes.Length) throw new ArgumentException("Exact release/asset binding required");
            var feed=ReleaseFeedPolicy.For(channel);
            if(!feed.Configured) throw new ArgumentException("Verified repository identity is required");
            var publicRelease=!localInstall && ReleaseFeedPolicy.IsPublicChannel(channel);
            var text=new StringBuilder((localInstall?"BetterAstralParty.LocalInstallDescriptor/v1":"BetterAstralParty.UpdateDescriptor/v4")+"\nrepository="+feed.Repository+"\nversion="+version
                +"\nchannel="+(publicRelease?"Stable":channel)+"\nplatform="+UpdateTrust.PlatformId+"\nrelease-id="+releaseId.ToString(CultureInfo.InvariantCulture)
                +"\nasset-id="+assetId.ToString(CultureInfo.InvariantCulture)+"\nasset-name=BetterAstralParty-"+version+"-update.zip\nzip-bytes="
                +zip.Length.ToString(CultureInfo.InvariantCulture)+"\nzip-sha256="+UpdateTrust.Hash(zip)+"\nfile-count="+InstallReceipt.Owned.Length.ToString(CultureInfo.InvariantCulture)+"\npurpose="+(localInstall?"local-install/"+channel:publicRelease?ReleaseFeedPolicy.PublicPurpose:feed.Purpose)
                +"\nrepository-id="+feed.RepositoryId.ToString(CultureInfo.InvariantCulture)+"\nsettings-schema=1\n");
            for(var i=0;i<sizes.Length;i++){
                if(sizes[i]<=0 || sizes[i]>UpdateTrust.MaxFileBytes || hashes[i].Length!=64) throw new ArgumentException("Invalid payload");
                foreach(var c in hashes[i]) if(!(c>='0'&&c<='9'||c>='A'&&c<='F')) throw new ArgumentException("Invalid payload hash");
                text.Append("file=").Append(InstallReceipt.Owned[i]).Append('\t').Append(sizes[i].ToString(CultureInfo.InvariantCulture)).Append('\t').Append(hashes[i]).Append('\n');
            }
            return Encoding.ASCII.GetBytes(text.ToString());
        }
    }
}
