using System;
using System.Threading;
using NUnit.Framework;
using SimplyLocalize.Integrations;

namespace SimplyLocalize.Tests
{
    public class AddressableTableProviderTests
    {
        private static LocalizationSourceReference Source(LocalizationSourceKind kind) =>
            new LocalizationSourceReference(AddressableTableProvider.Id, "test-table", kind);

        [Test]
        public void DefaultRegistryAndCopyLoadWholeTables()
        {
            var registry = new LocalizationSourceRegistry();
            foreach (var current in new[] { registry, registry.Copy() })
            {
                var capabilities = current.GetCapabilities(Source(LocalizationSourceKind.AssetTable));
                Assert.AreEqual(LocalizationLoadUnit.Table, capabilities.LoadUnit);
                Assert.IsTrue(capabilities.LoadsDirectDependencies);
                Assert.IsFalse(capabilities.CanCancelUnderlyingLoad);
            }
        }

        [Test]
        public void ExplicitlyEmptyRegistryStaysEmpty()
        {
            var registry = new LocalizationSourceRegistry(false);
            Assert.Throws<InvalidOperationException>(() => registry.GetCapabilities(Source(LocalizationSourceKind.AssetTable)));
        }

        [TestCase(LocalizationSourceKind.TextTable)]
        [TestCase(LocalizationSourceKind.Appearance)]
        public void UnsupportedKindsAreRejected(LocalizationSourceKind kind)
        {
            Assert.Throws<NotSupportedException>(() => new AddressableTableProvider().GetCapabilities(kind));
        }

        [Test]
        public void PreCancelledLoadDoesNotContactCatalog()
        {
            var request = new LocalizationSourceRequest(Source(LocalizationSourceKind.AssetTable), "en");
            var load = new AddressableTableProvider().LoadAsync(request, new CancellationToken(true));
            Assert.IsTrue(load.IsCanceled);
        }
    }
}
