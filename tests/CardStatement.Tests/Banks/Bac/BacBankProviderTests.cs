using System;
using System.IO;
using System.Linq;
using CardStatement.Core.Banks.Bac;
using CardStatement.Core.Pdf;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Banks.Bac;

public sealed class BacBankProviderTests
{
    private const string SamplePath = "../../../../../samples/final5140_45178439_316493_0.pdf";

    [Fact]
    public void Provider_ExposesCorrectIdentityAndResolvesSample()
    {
        // Arrange
        File.Exists(SamplePath).Should().BeTrue($"sample PDF must exist at {Path.GetFullPath(SamplePath)}");
        var words = new PdfPigExtractor().Extract(SamplePath);
        var provider = new BacBankProvider();

        // Assert Identity
        provider.Info.Id.Should().Be("bac");
        provider.Info.DisplayName.Should().Be("BAC Credomatic (El Salvador)");

        // Assert Detection
        var detectResult = provider.Detect(words);
        detectResult.Matched.Should().BeTrue();

        // Assert Parsing
        var statement = provider.Parse(words);
        statement.Should().NotBeNull();
        statement.CardType.Should().Be("VISA INFINITE BLACK");
        statement.MaskedAccount.Should().Be("4593-78XX-XXXX-2145");
        statement.PageCount.Should().Be(5);
        statement.PrintedTotalCharges.Should().Be(1462.19m);
        statement.PrintedTotalCredits.Should().Be(877.01m);
        statement.Sections.Should().HaveCount(5);
        statement.Sections.Select(s => s.CardLast4).Should().BeEquivalentTo(["2533", "2640", "2706", "4941", "5468"]);
    }
}
