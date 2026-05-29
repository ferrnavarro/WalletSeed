using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Banks.Bac;

public sealed class BacBankProvider : IBankProvider
{
    private static readonly BankInfo TheBank = new("bac", "BAC Credomatic (El Salvador)");

    private readonly BacDetector _detector;
    private readonly BacStatementParser _parser;

    public BankInfo Info => TheBank;

    public BacBankProvider()
    {
        _detector = new BacDetector();
        _parser = new BacStatementParser();
    }

    public BankDetection Detect(PdfDocumentWords words) => _detector.Detect(words);

    public Statement Parse(PdfDocumentWords words) => _parser.Parse(words);
}
