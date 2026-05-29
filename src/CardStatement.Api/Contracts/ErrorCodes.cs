namespace CardStatement.Api.Contracts;

public static class ErrorCodes
{
    public const string InvalidFileType = "INVALID_FILE_TYPE";
    public const string EmptyFile = "EMPTY_FILE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string PasswordProtected = "PASSWORD_PROTECTED";
    public const string NoTextExtractable = "NO_TEXT_EXTRACTABLE";
    public const string UnrecognizedLayout = "UNRECOGNIZED_LAYOUT";
    public const string ParseFailed = "PARSE_FAILED";
}
