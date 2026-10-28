using Microsoft.AspNetCore.Identity;
using Crown.Contracts.Auth;
using Crown.Contracts.Common;

namespace Crown.Infrastructure.Api;

public static class ApiErrors
{
    public static ApiErrorResponse FromCode(string code) =>
        new()
        {
            Code = code,
            Codes = [code]
        };

    public static ApiErrorResponse FromIdentityErrors(IEnumerable<IdentityError> errors)
    {
        var errorList = errors.ToList();
        var codes = errorList
            .Select(MapIdentityErrorCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ApiErrorResponse
        {
            Code = codes.FirstOrDefault(),
            Codes = codes,
            Message = string.Join(" ", errorList.Select(x => x.Description))
        };
    }

    public static ApiErrorResponse FromArgumentException(ArgumentException exception) =>
        FromCode(MapArgumentMessage(exception.Message));

    private static string MapIdentityErrorCode(IdentityError error) =>
        error.Code switch
        {
            "PasswordTooShort" => ApiErrorCodes.PasswordTooShort,
            "PasswordRequiresDigit" => ApiErrorCodes.PasswordRequiresDigit,
            "PasswordRequiresLower" => ApiErrorCodes.PasswordRequiresLower,
            "PasswordRequiresUpper" => ApiErrorCodes.PasswordRequiresUpper,
            "PasswordRequiresNonAlphanumeric" => ApiErrorCodes.PasswordRequiresNonAlphanumeric,
            "DuplicateUserName" => ApiErrorCodes.DuplicateUserName,
            "DuplicateEmail" => ApiErrorCodes.DuplicateEmail,
            "InvalidUserName" => ApiErrorCodes.InvalidUserName,
            _ => error.Code
        };

    private static string MapArgumentMessage(string message) =>
        message switch
        {
            "Unsupported currency code." => ApiErrorCodes.UnsupportedCurrency,
            "Invalid time zone id." => ApiErrorCodes.InvalidTimeZone,
            "Unsupported logo content type." => ApiErrorCodes.UnsupportedLogoContentType,
            "Logo data is required." => ApiErrorCodes.LogoDataRequired,
            "Invalid logo data." => ApiErrorCodes.InvalidLogoData,
            "Logo data is not a valid PNG, JPEG, or WEBP image." => ApiErrorCodes.InvalidLogoFormat,
            "Logo content type does not match the actual image format." => ApiErrorCodes.LogoContentTypeMismatch,
            _ when message.StartsWith("Logo size must be between", StringComparison.Ordinal) => ApiErrorCodes.InvalidLogoSize,
            _ => message
        };
}
