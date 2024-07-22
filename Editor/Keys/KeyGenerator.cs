#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Runtime.Data.Keys;
using UnityEditor;

namespace SimplyLocalize.Editor.Keys
{
    public static class KeyGenerator
    {
        private static IEnumerable<EnumHolder> _enums;
        private static string _enumKeysName;

        // private static readonly string FilePathAndName = "Packages/com.renkoff.simply-localize/Runtime/Data/Keys/Generated";
        private static readonly string FilePathAndName = "Assets/SimplyLocalize/Runtime/Data/Keys/Generated";

        private static readonly string FileExtension = ".cs";

        public static void SetEnums(IEnumerable<EnumHolder> enumEntries)
        {
            var enumHolders = enumEntries.ToList();
            if (enumHolders.Count == 0)
            {
                enumHolders.Add(new EnumHolder { Name = "None" });
            }

            _enums = enumHolders;
        }

        public static void GenerateEnumKeys(string fileName)
        {
            _enumKeysName = fileName;
            var path = Path.Combine(FilePathAndName, fileName);
            path = Path.ChangeExtension(path, FileExtension);

            using (var streamWriter = new StreamWriter(path))
            {
                streamWriter.WriteLine("using UnityEngine;\n");
                streamWriter.WriteLine($"namespace {nameof(SimplyLocalize)}.Runtime.Data.Keys.Generated\n{{");
                streamWriter.WriteLine("\tpublic enum " + fileName);
                streamWriter.WriteLine("\t{");
                foreach (var e in _enums)
                {
                    var inspectorName = e.InspectorName;
                    var markedInspectorName = e.MarkAsFormattable ? $"#F {(string.IsNullOrEmpty(inspectorName) ? e.Name : inspectorName)}" : inspectorName;

                    if (string.IsNullOrEmpty(markedInspectorName))
                    {
                        streamWriter.WriteLine("\t\t" + e.Name + ", ");
                    }
                    else
                    {
                        streamWriter.WriteLine("\t\t" + $"[InspectorName(\"{markedInspectorName}\")] " + e.Name + ", ");
                    }
                }

                streamWriter.WriteLine("\t}");
                streamWriter.WriteLine("}");
            }

            AssetDatabase.Refresh();
        }

        public static void GenerateDictionaryKeys(string fileName)
        {
            var path = Path.Combine(FilePathAndName, fileName);
            path = Path.ChangeExtension(path, FileExtension);

            using (var streamWriter = new StreamWriter(path))
            {
                streamWriter.WriteLine(
                    $"using System.Collections.Generic;\n\n" +
                    $"namespace {nameof(SimplyLocalize)}.Runtime.Data.Keys.Generated\n" +
                    $"{{\n" +
                    $"\tpublic static class {fileName}\n" +
                    $"\t{{\n" +
                    $"\t\tpublic static readonly Dictionary<{_enumKeysName}, string> Keys = new()\n" +
                    $"\t\t{{"
                );

                var keys = _enums.Select(e => $"\t\t\t{{{_enumKeysName}.{e.Name}, {_enumKeysName}.{e.Name}.ToString()}},");
                streamWriter.Write(string.Join(Environment.NewLine, keys));

                streamWriter.WriteLine(
                    $"\n\t\t}};\n" +
                    $"\t}}\n" +
                    $"}}"
                );
            }

            AssetDatabase.Refresh();
        }
    }
}
#endif