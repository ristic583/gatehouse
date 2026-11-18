namespace Gatehouse.Domain.Common;

public static class CardNumberMask
{
    public static string Mask(string? cardNumber)
    {
        if (cardNumber is null || cardNumber.Length < 4)
        {
            return "****";
        }

        return string.Concat("****", cardNumber.AsSpan(cardNumber.Length - 4));
    }
}
