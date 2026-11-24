namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Optional extension for rules that support fractions and counts outside Int32.
    /// Decimal scale is significant: 1m and 1.0m have different CLDR v operands.
    /// Existing IPluralRule implementations remain supported without changes.
    /// </summary>
    public interface IDecimalPluralRule : IPluralRule
    {
        int Evaluate(decimal count);
    }
}
