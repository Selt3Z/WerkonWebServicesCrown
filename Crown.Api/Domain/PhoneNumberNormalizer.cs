using System.Text;

namespace Crown.Domain;

/// <summary>Нормализация телефонов как в Ratchet: РФ → +7XXXXXXXXXX.</summary>
public static class PhoneNumberNormalizer
{
    public static bool TryNormalize(
        string? input,
        out string normalized,
        out string? errorMessage)
    {
        normalized = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Укажите телефон.";
            return false;
        }

        return TryNormalizeRussian(input, out normalized, out errorMessage);
    }

    /// <summary>Показ: +7 (912) 345-67-89. Невалидное — как есть.</summary>
    public static string FormatDisplay(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        var digits = ExtractDigits(stored);
        if (digits.Length == 11 && digits[0] == '7')
        {
            return $"+7 ({digits[1..4]}) {digits[4..7]}-{digits[7..9]}-{digits[9..11]}";
        }

        return stored.Trim();
    }

    private static bool TryNormalizeRussian(string input, out string normalized, out string? errorMessage)
    {
        var digits = ExtractDigits(input);

        if (digits.Length == 11 && digits.StartsWith('8'))
        {
            digits = "7" + digits[1..];
        }

        if (digits.Length == 10)
        {
            digits = "7" + digits;
        }

        if (digits.Length != 11 || digits[0] != '7')
        {
            normalized = string.Empty;
            errorMessage = "Некорректный телефон. Укажите российский номер (+7 и 10 цифр).";
            return false;
        }

        normalized = "+" + digits;
        errorMessage = null;
        return true;
    }

    private static string ExtractDigits(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            if (char.IsDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
