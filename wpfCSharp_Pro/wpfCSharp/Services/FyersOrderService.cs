using WpfCSharp.Models;
namespace WpfCSharp.Services;
public class FyersOrderService
{
    public Task<OrderTicket> PlaceOrderAsync(OrderTicket ticket, CancellationToken cancellationToken)
    {
        ticket.Status = "Blocked: Live order service disabled";
        return Task.FromResult(ticket);
    }
}
