namespace WpfCSharp.Models;
public class OrderTicket
{
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = "BUY";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "Paper";
}
