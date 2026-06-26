using CardStatement.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace CardStatement.Api.Endpoints;

public static class PdfUploadGuard
{
    public static IResult? Check(IFormFile? file, IConfiguration config, ILogger log)
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new ExtractionErrorResponse(
                new ErrorBody(ErrorCodes.EmptyFile, "The selected file is empty.")));
        }

        var maxBytes = config.GetValue<long>("Upload:MaxBytes");
        if (file.Length > maxBytes)
        {
            return Results.Json(new ExtractionErrorResponse(
                new ErrorBody(ErrorCodes.FileTooLarge, "This file exceeds the 25 MB limit.")), statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            using var sniffStream = file.OpenReadStream();
            byte[] buffer = new byte[5];
            int read = sniffStream.Read(buffer, 0, 5);
            if (read < 5 || buffer[0] != 0x25 || buffer[1] != 0x50 || buffer[2] != 0x44 || buffer[3] != 0x46 || buffer[4] != 0x2d)
            {
                return Results.BadRequest(new ExtractionErrorResponse(
                    new ErrorBody(ErrorCodes.InvalidFileType, "Please upload a PDF file.")));
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to read magic bytes from upload stream");
            return Results.BadRequest(new ExtractionErrorResponse(
                new ErrorBody(ErrorCodes.InvalidFileType, "Please upload a PDF file.")));
        }

        return null;
    }
}
