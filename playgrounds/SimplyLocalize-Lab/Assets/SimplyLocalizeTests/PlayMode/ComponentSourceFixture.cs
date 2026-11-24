using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace SimplyLocalize.Tests
{
    // In-memory source for component regressions. No Resources I/O or demo dependencies.
    internal sealed class ComponentSourceFixture : ILocalizationSourceProvider
    {
        internal string Text = "Text";
        internal UnityEngine.Object Icon;
        internal bool FailAssets;
        internal readonly Dictionary<string, LocalizationAppearance> Appearances = new();
        internal int Loads;
        internal int Releases;
        internal Action OnRelease;
        internal LocalizationSourceRegistry Registry()
        {
            var registry = new LocalizationSourceRegistry(); registry.Register("fixture", this); return registry;
        }
        internal LocalizationProjectConfig CreateConfig(params string[] languages)
        {
            var config = LocalizationProjectConfig.Create(languages);
            config.Sets[0].AddEntry("label", LocalizationEntryKind.Text);
            config.Sets[0].AddEntry("icon", LocalizationEntryKind.Asset);
            foreach (var language in config.Languages)
            {
                config.Sets[0].SetSources(language.Code,
                    new LocalizationSourceReference("fixture", "text", LocalizationSourceKind.TextTable),
                    new LocalizationSourceReference("fixture", "assets", LocalizationSourceKind.AssetTable));
                if (Appearances.ContainsKey(language.Code))
                    language.Appearance = new LocalizationSourceReference("fixture", "appearance", LocalizationSourceKind.Appearance);
            }
            return config;
        }
        public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) =>
            new(LocalizationLoadUnit.Table, true, true);
        public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Loads++;
            LocalizationSourceData data;
            switch (request.Source.Kind)
            {
                case LocalizationSourceKind.Appearance:
                    data = LocalizationSourceData.FromAppearance(Appearances[request.Language]); break;
                case LocalizationSourceKind.AssetTable:
                    if (FailAssets) throw new InvalidOperationException("Simulated asset load failure");
                    var assets = new Dictionary<string, UnityEngine.Object>();
                    if (Icon != null) assets.Add("icon", Icon);
                    data = LocalizationSourceData.FromAssets(assets); break;
                default: data = LocalizationSourceData.FromText(new Dictionary<string, string> { ["label"] = Text }); break;
            }
            return Task.FromResult(new LocalizationSourceLease(data, () => { Releases++; OnRelease?.Invoke(); return Task.CompletedTask; }));
        }
        // This assertion rejects unexpectedly asynchronous test operations instead of blocking Unity.
        internal static void Completed(Task operation)
        {
            Assert.That(operation.IsCompleted, Is.True, "The in-memory fixture must complete without I/O.");
            operation.GetAwaiter().GetResult();
        }
    }
}
