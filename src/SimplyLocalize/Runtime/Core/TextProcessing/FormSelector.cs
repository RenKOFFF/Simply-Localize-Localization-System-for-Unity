using System.Globalization;

namespace SimplyLocalize.TextProcessing
{
    /// <summary>
    /// Explicit zero-based choice for an existing {parameter|first|second|...} token.
    /// Pass new FormSelector(1) as an indexed or named argument to select the second form
    /// independently of the language. Ordinary numbers continue to use plural rules.
    /// Out-of-range indices clamp to the first/last available form, like plural results.
    /// </summary>
    public readonly struct FormSelector
    {
        public int Index { get; }
        public FormSelector(int index) => Index = index;
        public override string ToString() => Index.ToString(CultureInfo.InvariantCulture);
    }
}
