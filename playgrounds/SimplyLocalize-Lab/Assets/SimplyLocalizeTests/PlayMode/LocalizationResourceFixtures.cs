namespace SimplyLocalize.Tests
{
    // Addresses only: retaining this helper must never pin a Unity resource.
    internal static class LocalizationResourceFixtures
    {
        internal const string VoiceRoot = "SimplyLocalizeTests/Lifecycle/Voice";
        internal const string ImageTable = "SimplyLocalizeTests/Lifecycle/Images/en/Image";
        internal const string PrimaryKey = "audio/primary";
        internal const string FallbackKey = "audio/fallback";

        internal static string VoiceTable(string language) => VoiceRoot + "/" + language + "/Clips";
    }
}
