using CardStatement.Api.Contracts;
using CardStatement.Api.Endpoints;
using CardStatement.Api.ErrorHandling;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CardStatement.Api.Wallet;

public static class WalletImportEndpoint
{
    public static void MapWalletImport(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/wallet/accounts", async Task<IResult> (IWalletApiClient walletClient, CancellationToken ct) =>
        {
            try
            {
                var accounts = await walletClient.ListAccountsAsync(ct);
                return Results.Ok(new { accounts = accounts.Where(a => !a.Archived).Select(a => new { a.Id, a.Name, a.CurrencyCode, a.AccountType }) });
            }
            catch (WalletApiException ex)
            {
                return WalletErrorMapper.ToResult(ex);
            }
        })
        .DisableAntiforgery();

        app.MapGet("/api/wallet/categories", async Task<IResult> (IWalletApiClient walletClient, CancellationToken ct) =>
        {
            try
            {
                var categories = await walletClient.ListCategoriesAsync(ct);
                return Results.Ok(new { categories = categories.Select(c => new { c.Id, c.Name, c.Color }) });
            }
            catch (WalletApiException ex)
            {
                return WalletErrorMapper.ToResult(ex);
            }
        })
        .DisableAntiforgery();

        app.MapPost("/api/wallet/import/compare", async Task<IResult> (
            [FromForm] IFormFile? file,
            [FromForm] string accountId,
            IWalletApiClient walletClient,
            WalletImportService service,
            IConfiguration config,
            ILogger<Program> log,
            CancellationToken ct) =>
        {
            try
            {
                var guardFailure = PdfUploadGuard.Check(file, config, log);
                if (guardFailure is not null)
                {
                    return guardFailure;
                }

                if (file is null)
                {
                    return Results.BadRequest(new ExtractionErrorResponse(new ErrorBody(ErrorCodes.EmptyFile, "The selected file is empty.")));
                }

                using var tempFile = new TempPdfFile(file);
                return Results.Ok(await service.CompareAsync(tempFile.Path, accountId, ct));
            }
            catch (WalletApiException ex)
            {
                return WalletErrorMapper.ToResult(ex);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Compare endpoint failed with exception");
                var result = ExtractionFailureMapper.TryMapKnown(ex);
                if (result is not null)
                {
                    return result;
                }

                return Results.Json(new ExtractionErrorResponse(new ErrorBody(ErrorCodes.ParseFailed, "Something went wrong while reading this PDF. Please try again.")), statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .DisableAntiforgery();

        app.MapPost("/api/wallet/import/submit", async Task<IResult> (SubmitRequest request, WalletImportService service, CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.AccountId))
                {
                    return Results.BadRequest(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.Rejected, "accountId is required.")));
                }

                if (request.Rows.Count == 0)
                {
                    return Results.BadRequest(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.Rejected, "At least one row is required.")));
                }

                if (request.Rows.Any(r => string.IsNullOrWhiteSpace(r.CategoryId)))
                {
                    var indices = string.Join(", ", request.Rows.Where(r => string.IsNullOrWhiteSpace(r.CategoryId)).Select(r => r.Index));
                    return Results.BadRequest(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.Rejected, $"Missing categoryId for row indices [{indices}]")));
                }

                return Results.Ok(await service.SubmitAsync(request, ct));
            }
            catch (WalletApiException ex)
            {
                return WalletErrorMapper.ToResult(ex);
            }
        })
        .DisableAntiforgery();
    }
}
