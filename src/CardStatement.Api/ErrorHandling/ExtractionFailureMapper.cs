using CardStatement.Api.Contracts;

namespace CardStatement.Api.ErrorHandling;

public static class ExtractionFailureMapper
{
    public static IResult? TryMapKnown(Exception ex)
    {
        var typeName = ex.GetType().Name;
        
        if (typeName == "PdfDocumentOpenException" || 
            ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || 
            ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) ||
            ex.InnerException?.Message.Contains("password", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Results.UnprocessableEntity(new ExtractionErrorResponse(
                new ErrorBody(ErrorCodes.PasswordProtected, "This PDF is password-protected. Please remove the password and try again.")
            ));
        }
        
        if (ex is NoTextExtractableException)
        {
            return Results.UnprocessableEntity(new ExtractionErrorResponse(
                new ErrorBody(ErrorCodes.NoTextExtractable, "This PDF doesn't contain machine-readable text. Scanned PDFs aren't supported in this version.")
            ));
        }

        if (ex is UnrecognizedLayoutException)
        {
            return Results.UnprocessableEntity(new ExtractionErrorResponse(
                new ErrorBody(ErrorCodes.UnrecognizedLayout, "We couldn't recognize this as a BAC Credomatic statement.")
            ));
        }

        return null;
    }
}
