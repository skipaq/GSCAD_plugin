using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GeoScheme.AutoCAD
{
    [DataContract]
    public sealed class TextBlockItem
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }
    }

    [DataContract]
    internal sealed class TextBlockStore
    {
        [DataMember(Name = "items")]
        public List<TextBlockItem> Items { get; set; } = new List<TextBlockItem>();
    }

    /// <summary>Библиотека текстовых блоков в профиле пользователя.</summary>
    public static class TextBlockLibrary
    {
        public static string Folder
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "GeoScheme");
            }
        }

        public static string FilePath => Path.Combine(Folder, "TextBlocks.json");

        public static List<TextBlockItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<TextBlockItem>();

                using (var stream = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(TextBlockStore));
                    var store = serializer.ReadObject(stream) as TextBlockStore;
                    return store?.Items ?? new List<TextBlockItem>();
                }
            }
            catch
            {
                return new List<TextBlockItem>();
            }
        }

        public static void Save(List<TextBlockItem> blocks)
        {
            try
            {
                Directory.CreateDirectory(Folder);

                using (var stream = File.Create(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(TextBlockStore));
                    serializer.WriteObject(stream, new TextBlockStore { Items = blocks });
                }
            }
            catch
            {
                // не смогли сохранить — библиотека останется в памяти
            }
        }
    }
}
