using System;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Marks a string field as a localization key.
    /// Replaces the default text field with the localization key search dropdown.
    /// Selects existing text keys from Main's default set. Create entries in the Simply Localize window.
    /// Use LocalizationEntryReference for other channels/sets and rename-safe bindings.
    ///
    /// Usage:
    ///   [LocalizationKey]
    ///   public string myKey;
    ///
    ///   [LocalizationKey]
    ///   [SerializeField] private string _dialogueKey;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class LocalizationKeyAttribute : PropertyAttribute { }

    /// <summary>
    /// Shows a read-only translation preview below a string field marked with [LocalizationKey].
    /// Can be used standalone on any string field that contains a localization key.
    ///
    /// Usage:
    ///   [LocalizationKey]
    ///   [LocalizationPreview]
    ///   public string myKey;
    ///
    ///   [LocalizationKey]
    ///   [LocalizationPreview(showAllLanguages: true)]
    ///   public string myKey;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class LocalizationPreviewAttribute : PropertyAttribute
    {
        public bool ShowAllLanguages { get; }

        public LocalizationPreviewAttribute(bool showAllLanguages = false)
        {
            ShowAllLanguages = showAllLanguages;
        }
    }
}
