using System;

namespace CardStatement.Core.Banks;

public sealed record BankDetection
{
    public const int HighConfidence = 90;
    public const int MediumConfidence = 50;
    public const int LowConfidence = 10;

    public bool Matched { get; }
    public int Confidence { get; }
    public string? Reason { get; }

    public BankDetection(bool matched, int confidence, string? reason)
    {
        if (matched)
        {
            if (confidence < 1 || confidence > 100)
                throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 1 and 100 when matched is true.");
        }
        else
        {
            if (confidence != 0)
                throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be 0 when matched is false.");
        }

        Matched = matched;
        Confidence = confidence;
        Reason = reason;
    }

    public static BankDetection NoMatch(string? reason = null) =>
        new(matched: false, confidence: 0, reason: reason);

    public static BankDetection Match(int confidence, string? reason = null) =>
        new(matched: true, confidence: confidence, reason: reason);
}
