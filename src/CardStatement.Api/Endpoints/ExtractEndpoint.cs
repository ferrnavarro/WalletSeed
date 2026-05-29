using CardStatement.Core.Abstractions;
using CardStatement.Api.Contracts;
using CardStatement.Api.ErrorHandling;
using CardStatement.Api.Mapping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CardStatement.Api.Endpoints;

public static class ExtractEndpoint
{
    public static void MapExtract(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/statements/extract", async (
            IFormFile file,
            IPdfExtractor pdf,
            IStatementParser parser,
            IReconciler reconciler,
            IConfiguration config,
            ILogger<Program> log) =>
        {
            // T063: Guard file null or empty
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ExtractionErrorResponse(
                    new ErrorBody(ErrorCodes.EmptyFile, "The selected file is empty.")
                ));
            }

            // T063: Guard file size limit
            var maxBytes = config.GetValue<long>("Upload:MaxBytes");
            if (file.Length > maxBytes)
            {
                return Results.Json(new ExtractionErrorResponse(
                    new ErrorBody(ErrorCodes.FileTooLarge, "This file exceeds the 25 MB limit.")
                ), statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            // T064: Magic-byte sniff %PDF-
            try
            {
                using var sniffStream = file.OpenReadStream();
                byte[] buffer = new byte[5];
                int read = sniffStream.Read(buffer, 0, 5);
                if (read < 5 || buffer[0] != 0x25 || buffer[1] != 0x50 || buffer[2] != 0x44 || buffer[3] != 0x46 || buffer[4] != 0x2d)
                {
                    return Results.BadRequest(new ExtractionErrorResponse(
                        new ErrorBody(ErrorCodes.InvalidFileType, "Please upload a PDF file.")
                    ));
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to read magic bytes from upload stream");
                return Results.BadRequest(new ExtractionErrorResponse(
                    new ErrorBody(ErrorCodes.InvalidFileType, "Please upload a PDF file.")
                ));
            }

            // Log exit metadata without logging PII (R9 Constraint)
            log.LogInformation("Processing upload: {FileName}, Size: {FileSize} bytes", 
                System.IO.Path.GetFileName(file.FileName), file.Length);

            using var tempFile = new TempPdfFile(file);

            try
            {
                // Extract PDF words
                var words = pdf.Extract(tempFile.Path);
                
                // US3: Check if scanned PDF (no extractable text)
                if (words.Words == null || words.Words.Count == 0)
                {
                    throw new NoTextExtractableException("No text words found in PDF");
                }

                // Parse statement
                CardStatement.Core.Models.Statement statement;
                try
                {
                    statement = parser.Parse(words);
                }
                catch (Exception parseEx)
                {
                    throw new UnrecognizedLayoutException("Failed to parse statement layout", parseEx);
                }
                
                // US3: Check if unrecognized layout (no cardholder sections and no transactions)
                if (statement.Sections == null || statement.Sections.Count == 0 || !statement.Sections.SelectMany(s => s.Transactions).Any())
                {
                    throw new UnrecognizedLayoutException("No cardholder sections or transactions found in parsed statement");
                }

                var reconciled = reconciler.Reconcile(statement);
                var response = StatementMapper.ToResponse(reconciled);

                log.LogInformation("Successfully processed statement. Pages: {PageCount}, Sections: {SectionCount}", 
                    reconciled.PageCount, reconciled.Sections.Count);

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                // T065 & T066: Map exception to known responses (422)
                var result = ExtractionFailureMapper.TryMapKnown(ex);
                if (result != null)
                {
                    log.LogWarning(ex, "Known extraction error: {Message}", ex.Message);
                    return result;
                }

                // Fallback 500 error code
                log.LogError(ex, "Catch-all error during statement extraction");
                return Results.Json(new ExtractionErrorResponse(
                    new ErrorBody(ErrorCodes.ParseFailed, "Something went wrong while reading this PDF. Please try again.")
                ), statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .DisableAntiforgery();
    }
}
