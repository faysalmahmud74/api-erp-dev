namespace Ordernest.Backend.Models;

public enum OrderStatus
{
    Pending = 0,
    Completed = 1,
    Cancelled = 2,
    Refunded = 3
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Mobile = 2,
    Other = 3
}
