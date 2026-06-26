using CardStatement.Api.Contracts;
using CardStatement.Api.Wallet.Contracts;

namespace CardStatement.Api.Wallet;

public static class WalletErrorMapper
{
    public static IResult ToResult(WalletApiException ex)
    {
        return ex.Kind switch
        {
            WalletApiErrorKind.NotConfigured => Results.Json(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.NotConfigured, "Wallet integration is not configured. Please set the Wallet JWT and base URL.")), statusCode: StatusCodes.Status503ServiceUnavailable),
            WalletApiErrorKind.CredentialsInvalid => Results.Json(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.CredentialsInvalid, "The Wallet credentials configured by the operator are invalid.")), statusCode: StatusCodes.Status502BadGateway),
            WalletApiErrorKind.Unavailable => Results.Json(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.Unavailable, "The Wallet service is unavailable right now. Please try again shortly.")), statusCode: StatusCodes.Status504GatewayTimeout),
            WalletApiErrorKind.Rejected => Results.Json(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.Rejected, "The Wallet request was rejected by the service.")), statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Json(new WalletErrorResponse(new ErrorBody(WalletErrorCodes.Unavailable, "The Wallet service is unavailable right now. Please try again shortly.")), statusCode: StatusCodes.Status504GatewayTimeout)
        };
    }
}
