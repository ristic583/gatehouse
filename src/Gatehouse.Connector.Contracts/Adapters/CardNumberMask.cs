using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public static class CardNumberMask
{
    public static string Mask(string cardNumber) =>
        cardNumber.Length < 4 ? "****" : "****" + cardNumber[^4..];
}
