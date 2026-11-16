using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// Optional asynchronous preparation contract. Existing synchronous providers need not implement it.
    /// Invoked on the manager's Unity thread; Unity object work must remain on the Unity thread.
    /// Assets/provider remain caller-owned, exactly as for ILocalizationDataProvider. On failure or
    /// cancellation the provider is responsible for its partial work; the manager never unloads assets.
    /// No thread-scoped logger context is held across awaits; asynchronous providers own their logging.
    /// </summary>
    public interface IAsyncLocalizationDataProvider : ILocalizationDataProvider
    {
        Task<Dictionary<string, string>> LoadTextDataAsync(string languageCode, CancellationToken cancellationToken);
        Task<List<LocalizationAssetTable>> LoadAssetTablesAsync(string languageCode, CancellationToken cancellationToken);
    }
}
