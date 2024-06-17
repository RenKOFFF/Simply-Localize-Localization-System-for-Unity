#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace SimplyLocalize.Data.KeyGeneration
{
    public static class KeyGenerator
    {
        private static IEnumerable<EnumHolder> _enums;
        private static string _enumKeysName;

        private static readonly string FilePathAndName = $"Assets/{nameof(SimplyLocalize)}/Data/Keys/{0}.cs";

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
            var path = string.Format(FilePathAndName, fileName);
            using (var streamWriter = new StreamWriter(path))
            {
                streamWriter.WriteLine("using UnityEngine;\n");
                streamWriter.WriteLine($"namespace {nameof(SimplyLocalize)}.Data.Keys\n{{\n");
                streamWriter.WriteLine("\tpublic enum " + fileName);
                streamWriter.WriteLine("\t{");
                foreach (var e in _enums)
                {
                    var inspectorName = e.InspectorName;

                    if (string.IsNullOrEmpty(inspectorName))
                    {
                        streamWriter.WriteLine("\t\t" + e.Name + ", ");
                    }
                    else
                    {
                        streamWriter.WriteLine("\t\t" + $"[InspectorName(\"{inspectorName}\")]" + e.Name + ", ");
                    }
                }

                streamWriter.WriteLine("\t}");
                streamWriter.WriteLine("}");
            }
            
            AssetDatabase.Refresh();
        }

        public static void GenerateDictionaryKeys(string fileName)
        {
            var path = string.Format(FilePathAndName, fileName);
            using (var streamWriter = new StreamWriter(path))
            {
                streamWriter.WriteLine("using System.Collections.Generic;");
                streamWriter.WriteLine($"namespace {nameof(SimplyLocalize)}.Data.KeyGeneration\n{{");
                streamWriter.WriteLine("\tpublic static class " + fileName);
                streamWriter.WriteLine("\t{");
                streamWriter.WriteLine($"\t\tpublic static readonly Dictionary<{_enumKeysName}, string> Keys = new()");
                streamWriter.WriteLine("\t\t{");
                foreach (var e in _enums)
                {
                    streamWriter.Write("\n\t\t\t{");
                    streamWriter.Write($"{_enumKeysName}.{e.Name}" + ", " +
                                       $"{_enumKeysName}.{e.Name}.ToString()");
                    streamWriter.Write("},");
                }

                streamWriter.WriteLine("\n\t\t};");
                streamWriter.WriteLine("\t}");
                streamWriter.WriteLine("}");
            }

            AssetDatabase.Refresh();
        }
    }
}
#endif