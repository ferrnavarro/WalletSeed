using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardStatement.Api.Contracts;
using CardStatement.Api.Tests.Fixtures;
using FluentAssertions;

namespace CardStatement.Api.Tests;

public sealed class ExtractEndpointTests : IClassFixture<WebApiFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExtractEndpointTests(WebApiFactory factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private async Task<ExtractedStatementResponse> UploadSamplePdfAsync()
    {
        using var content = new MultipartFormDataContent();
        using var fileStream = SamplePdf.OpenRead();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        
        content.Add(streamContent, "file", Path.GetFileName(SamplePdf.Path));

        var response = await _client.PostAsync("/api/statements/extract", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ExtractedStatementResponse>(_jsonOptions);
        result.Should().NotBeNull();
        return result!;
    }

    [Fact]
    public async Task HappyPath_Returns200_WithStatementHeader()
    {
        // Act
        var result = await UploadSamplePdfAsync();

        // Assert (T041)
        var expected = GroundTruth.Data.Statement;
        result.Statement.CardType.Should().Be(expected.CardType);
        result.Statement.MaskedAccount.Should().Be(expected.MaskedAccount);
        result.Statement.Period.IssueDate.ToString("yyyy-MM-dd").Should().Be(expected.Period.IssueDate);
        result.Statement.Period.CutoffDate.ToString("yyyy-MM-dd").Should().Be(expected.Period.CutoffDate);
        result.Statement.PageCount.Should().Be(expected.PageCount);
    }

    [Fact]
    public async Task HappyPath_AllTransactions_RowForRow()
    {
        // Act
        var response = await UploadSamplePdfAsync();

        // Assert (T042)
        var expectedRecords = GroundTruth.Data.Records;

        // Flatten sections to get all transactions
        var actualRecords = response.Sections
            .SelectMany(s => s.Transactions)
            .ToList();

        actualRecords.Count.Should().Be(expectedRecords.Count);

        for (int i = 0; i < expectedRecords.Count; i++)
        {
            var actual = actualRecords[i];
            var expected = expectedRecords[i];

            actual.Date.ToString("yyyy-MM-dd").Should().Be(expected.Date);
            actual.Description.Should().Be(expected.Description);
            actual.Direction.ToString().ToLowerInvariant().Should().Be(expected.Direction.ToLowerInvariant());
            actual.Amount.Should().Be(expected.Amount);
            actual.CardLast4.Should().Be(expected.CardLast4);
            actual.NeedsReview.Should().Be(expected.NeedsReview);
            
            // Forward-compatibility fields must be null/false
            actual.CategoryId.Should().BeNull();
            actual.CategoryName.Should().BeNull();
            actual.LabelId.Should().BeNull();
            actual.LabelName.Should().BeNull();
            actual.LabelUnmapped.Should().BeFalse();
        }
    }

    [Fact]
    public async Task HappyPath_AttributesTransactionsToCorrectSection()
    {
        // Act
        var response = await UploadSamplePdfAsync();

        // Assert (T043)
        // Group transactions by cardholder section and assert they match
        response.Sections.Should().NotBeEmpty();

        foreach (var section in response.Sections)
        {
            section.CardLast4.Should().NotBeNullOrWhiteSpace().And.HaveLength(4);
            section.RawName.Should().NotBeNullOrWhiteSpace();

            foreach (var tx in section.Transactions)
            {
                tx.CardLast4.Should().Be(section.CardLast4);
            }
        }

        // FERNANDO MAGAÑA should appear as two separate sections (2706 and 5468)
        var sectionsWithName = response.Sections
            .Where(s => s.RawName.Contains("FERNANDO MAGAÑA", StringComparison.OrdinalIgnoreCase))
            .ToList();

        sectionsWithName.Count.Should().Be(2);
        sectionsWithName.Select(s => s.CardLast4).Should().Contain(new[] { "2706", "5468" });
    }

    [Fact]
    public async Task HappyPath_SectionTotals_MatchPrintedAndComputed()
    {
        // Act
        var response = await UploadSamplePdfAsync();

        // Assert (T055)
        foreach (var section in response.Sections)
        {
            if (section.Totals.PrintedCharges.HasValue)
            {
                section.Totals.ComputedCharges.Should().Be(section.Totals.PrintedCharges.Value);
            }
            if (section.Totals.PrintedCredits.HasValue)
            {
                section.Totals.ComputedCredits.Should().Be(section.Totals.PrintedCredits.Value);
            }
        }
    }

    [Fact]
    public async Task HappyPath_StatementTotals_MatchResultJson()
    {
        // Act
        var response = await UploadSamplePdfAsync();

        // Assert (T056)
        var expected = GroundTruth.Data.Totals;
        response.Totals.ComputedExpense.Should().Be(expected.Expense);
        response.Totals.ComputedIncome.Should().Be(expected.Income);
        
        response.Totals.PrintedExpense.Should().Be(expected.Expense);
        response.Totals.PrintedIncome.Should().Be(expected.Income);
    }

    [Fact]
    public async Task HappyPath_ReconciliationStatus_Ok()
    {
        // Act
        var response = await UploadSamplePdfAsync();

        // Assert (T057)
        var expectedReconciliation = GroundTruth.Data.ReconciliationStatus;
        response.ReconciliationStatus.ToString().ToLowerInvariant().Should().Be(expectedReconciliation.ToLowerInvariant());

        foreach (var section in response.Sections)
        {
            section.ReconciliationStatus.Should().Be(CardStatement.Core.Models.ReconciliationStatus.Ok);
        }
    }

    private async Task AssertErrorResponseAsync(string filename, HttpStatusCode expectedStatus, string expectedErrorCode)
    {
        var baseDir = AppContext.BaseDirectory;
        var relativePath = Path.Combine(baseDir, "..", "..", "..", "..", "..", "tests", "CardStatement.Api.Tests", "Fixtures", "Errors", filename);
        var absolutePath = Path.GetFullPath(relativePath);

        File.Exists(absolutePath).Should().BeTrue($"Error test file must exist at {absolutePath}");

        using var content = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(absolutePath);
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        
        content.Add(streamContent, "file", filename);

        var response = await _client.PostAsync("/api/statements/extract", content);
        response.StatusCode.Should().Be(expectedStatus);

        var errorResult = await response.Content.ReadFromJsonAsync<ExtractionErrorResponse>(_jsonOptions);
        errorResult.Should().NotBeNull();
        errorResult!.Error.Should().NotBeNull();
        errorResult.Error.Code.Should().Be(expectedErrorCode);
    }

    [Fact]
    public async Task Error_InvalidFileType_PlainText_Returns400()
    {
        await AssertErrorResponseAsync("plaintext.pdf", HttpStatusCode.BadRequest, ErrorCodes.InvalidFileType);
    }

    [Fact]
    public async Task Error_InvalidFileType_BadMagic_Returns400()
    {
        await AssertErrorResponseAsync("bad-magic.pdf", HttpStatusCode.BadRequest, ErrorCodes.InvalidFileType);
    }

    [Fact]
    public async Task Error_EmptyFile_Returns400()
    {
        await AssertErrorResponseAsync("empty.pdf", HttpStatusCode.BadRequest, ErrorCodes.EmptyFile);
    }

    [Fact]
    public async Task Error_PasswordProtected_Returns422()
    {
        await AssertErrorResponseAsync("encrypted.pdf", HttpStatusCode.UnprocessableEntity, ErrorCodes.PasswordProtected);
    }

    [Fact]
    public async Task Error_NoTextExtractable_Returns422()
    {
        await AssertErrorResponseAsync("scanned-no-text.pdf", HttpStatusCode.UnprocessableEntity, ErrorCodes.NoTextExtractable);
    }

    [Fact]
    public async Task Error_UnrecognizedLayout_Returns422()
    {
        await AssertErrorResponseAsync("wrong-bank-layout.pdf", HttpStatusCode.UnprocessableEntity, ErrorCodes.UnrecognizedLayout);
    }

    [Fact]
    public async Task Error_FileTooLarge_Returns413()
    {
        using var content = new MultipartFormDataContent();
        using var stream = new MockLargeStream();
        using var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        
        content.Add(streamContent, "file", "large.pdf");

        var response = await _client.PostAsync("/api/statements/extract", content);
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        var errorResult = await response.Content.ReadFromJsonAsync<ExtractionErrorResponse>(_jsonOptions);
        errorResult.Should().NotBeNull();
        errorResult!.Error.Should().NotBeNull();
        errorResult.Error.Code.Should().Be(ErrorCodes.FileTooLarge);
    }

    private sealed class MockLargeStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 26 * 1024 * 1024; // 26 MB
        public override long Position { get; set; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            while (bytesRead < count && Position < Length)
            {
                if (Position == 0 && bytesRead < count) buffer[offset + bytesRead++] = 0x25; // %
                else if (Position == 1 && bytesRead < count) buffer[offset + bytesRead++] = 0x50; // P
                else if (Position == 2 && bytesRead < count) buffer[offset + bytesRead++] = 0x44; // D
                else if (Position == 3 && bytesRead < count) buffer[offset + bytesRead++] = 0x46; // F
                else if (Position == 4 && bytesRead < count) buffer[offset + bytesRead++] = 0x2d; // -
                else
                {
                    if (bytesRead < count) buffer[offset + bytesRead++] = 0;
                }
                Position++;
            }
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
