public class Fund
{
    public string Name { get; set; } = "";
    public string ISIN { get; set; } = "";
    public string Currency { get; set; } = "";
    public List<NavQuote> NavQuotes { get; set; } = [];
    public record NavQuote(decimal Value, decimal ChangePercent, DateOnly Date);
}