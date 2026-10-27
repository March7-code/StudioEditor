using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuParsedMetadataCache
    {
        public static T Read<T>(KoikatsuBundleSource source, string identity, Func<T> parse)
        {
            var key = "metadata-v1|" + DiskCacheStore.SourceFingerprint(source.FilePath) + "|" +
                source.StreamOffset + "|" + source.ArchiveEntryName + "|" + identity;
            var serializer = new DataContractSerializer(typeof(T), new[]
            {
                typeof(KoikatsuDynamicBoneColliderDefinition[]),
            });
            var store = StudioEditorDiskCache.Store;
            if (store.TryRead("Metadata", key, out var bytes))
            {
                try
                {
                    using (var reader = XmlDictionaryReader.CreateBinaryReader(bytes, XmlDictionaryReaderQuotas.Max))
                    {
                        var value = (T)serializer.ReadObject(reader);
                        ReferenceModelLoadContext.Current?.RecordCache(true);
                        return value;
                    }
                }
                catch (SerializationException) { }
                catch (XmlException) { }
            }
            ReferenceModelLoadContext.Current?.RecordCache(false);
            var parsed = parse();
            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream, null, null, false))
                        serializer.WriteObject(writer, parsed);
                    store.Write("Metadata", key, stream.ToArray(),
                        ReferenceModelLoadContext.Current?.CancellationToken ?? default);
                }
            }
            catch (SerializationException) { }
            catch (InvalidDataContractException) { }
            return parsed;
        }
    }
}
