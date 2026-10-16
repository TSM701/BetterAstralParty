#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BetterAstralParty.Updating;
namespace BetterAstralParty.Packaging {
    internal sealed class UnsignedDistribution {
        internal readonly byte[] Zip,Descriptor,HelperDescriptor,HelperImage; internal readonly UpdateContext Context;
        internal UnsignedDistribution(UpdateContext context,byte[] zip,byte[] descriptor,byte[] helper,byte[] image) {Context=context;Zip=zip;Descriptor=descriptor;HelperDescriptor=helper;HelperImage=image;}
    }
    // Offline two-phase packaging. This tool never creates/imports private keys, signs, or publishes.
    internal static class UpdateDistribution {
        internal static UnsignedDistribution Prepare(UpdateContext context,IDictionary<string,byte[]> inputs,byte[] helperImage) {
            var version=UpdateVersion.Parse(context.ReleaseTag);
            if(context.Repository!="TSM701/BetterAstralParty" || version.CompareTo(UpdateVersion.Parse(context.CurrentVersion))<=0 || context.Channel=="Stable" && (version.Major==0 || version.IsPrerelease) || inputs.Count!=InstallReceipt.Owned.Length) throw new UpdateValidationException(UpdateFailure.WrongTarget);
            var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
            foreach(var path in InstallReceipt.Owned) {
                byte[] bytes;if(!inputs.TryGetValue(path,out bytes!) || bytes==null || bytes.Length==0 || bytes.LongLength>UpdateTrust.MaxFileBytes) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
                files.Add(path,(byte[])bytes.Clone());total+=bytes.LongLength;
            }
            if(total>UpdateTrust.MaxExpandedBytes || InstalledPluginMetadata.Read(files[UpdateTrust.PluginPath]).Version!=version.Tag) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            byte[] zip;
            using(var output=new MemoryStream()) {
                using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true)) foreach(var file in files) {
                    var entry=archive.CreateEntry(file.Key,CompressionLevel.NoCompression);entry.LastWriteTime=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
                    using(var target=entry.Open()) target.Write(file.Value,0,file.Value.Length);
                }
                zip=output.ToArray();
            }
            if(zip.LongLength>UpdateTrust.MaxZipBytes) throw new UpdateValidationException(UpdateFailure.LimitExceeded);
            var descriptor=new StringBuilder("BetterAstralParty.UpdateDescriptor/v1\nrepository="+context.Repository+"\nversion="+context.ReleaseTag+"\nchannel="+context.Channel+"\nplatform="+context.Platform+"\nrelease-id="+context.ReleaseId.ToString(CultureInfo.InvariantCulture)+"\nasset-id="+context.AssetId.ToString(CultureInfo.InvariantCulture)+"\nasset-name=BetterAstralParty-"+version.Tag+"-update.zip\nzip-bytes="+zip.LongLength.ToString(CultureInfo.InvariantCulture)+"\nzip-sha256="+UpdateTrust.Hash(zip)+"\nfile-count="+files.Count.ToString(CultureInfo.InvariantCulture)+"\n");
            foreach(var file in files) descriptor.Append("file=").Append(file.Key).Append('\t').Append(file.Value.LongLength.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(UpdateTrust.Hash(file.Value)).Append('\n');
            var image=(byte[])helperImage.Clone();
            return new UnsignedDistribution(context,zip,Encoding.ASCII.GetBytes(descriptor.ToString()),HelperDescriptor.Prepare(version.Tag,image),image);
        }
        internal static Dictionary<string,byte[]> Finalize(UnsignedDistribution request,byte[] signatureEnvelope,byte[] helperSignatureEnvelope,UpdateTrust trust) {
            if(!trust.Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
            var signature=DetachedSignature.Parse(signatureEnvelope);
            var verified=trust.VerifyDescriptor(request.Descriptor,signature.KeyId,signature.Signature,request.Context);
            if(verified.Files.Count!=InstallReceipt.Owned.Length) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            UpdatePackage.VerifyPayload(request.Zip,verified);
            HelperDescriptor.Verify(trust,request.HelperDescriptor,DetachedSignature.Parse(helperSignatureEnvelope),request.HelperImage,UpdateVersion.Parse(verified.Version).Tag);
            var stem="BetterAstralParty-"+UpdateVersion.Parse(request.Context.ReleaseTag).Tag+"-update";
            return new Dictionary<string,byte[]>(StringComparer.Ordinal) {
                {stem+".zip",(byte[])request.Zip.Clone()},{ReleaseFeedPolicy.UpdateManifestAsset,(byte[])request.Descriptor.Clone()},{ReleaseFeedPolicy.UpdateSignatureAsset,(byte[])signatureEnvelope.Clone()},
                {"helper.descriptor",(byte[])request.HelperDescriptor.Clone()},{"helper.signature",(byte[])helperSignatureEnvelope.Clone()},{HelperDescriptor.Image,(byte[])request.HelperImage.Clone()}
            };
        }
        internal static UnsignedDistribution ReadRequest(WindowsFileFence fence,UpdateContext context) {
            byte[] Read(string path,long max) {using(var file=fence.OpenFile(path)) return UpdateTicket.Read(file,max);}
            return new UnsignedDistribution(context,Read("payload.zip",UpdateTrust.MaxZipBytes),Read("update.descriptor",UpdateTrust.MaxDescriptorBytes),Read("helper.descriptor",UpdateTrust.MaxDescriptorBytes),Read(HelperDescriptor.Image,UpdateTrust.MaxFileBytes));
        }
        internal static void WriteNew(WindowsFileFence fence,IDictionary<string,byte[]> files) {
            // All bytes validated before a private caller-created output root is written.
            foreach(var item in files) using(var file=fence.OpenFile(item.Key,true,true)) {file.Stream.Write(item.Value,0,item.Value.Length);file.Flush();}
        }
    }
}

