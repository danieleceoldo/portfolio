using Microsoft.Playwright;
using System.Text.Json;

class Portfolio
{
    string FilePath { get; }
    Dictionary<string, Fund> Funds { get; set; } = [];
    
    public Portfolio(string name)
    {
        _ = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Portfolio name cannot be null or empty.", nameof(name)) : "";
        FilePath = $"{name}.json";
        if (File.Exists(FilePath))
        {
            Funds = JsonSerializer.Deserialize<Dictionary<string, Fund>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, Fund>();
        }
    }

    void Save()
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Funds, new JsonSerializerOptions { WriteIndented = true }));
    }   
    
    public async Task Add(string morningstarCode)
    {
        _ = string.IsNullOrWhiteSpace(morningstarCode) 
            ? throw new ArgumentException("Morningstar code cannot be null or empty.", nameof(morningstarCode)) : "";
        _ = Funds.ContainsKey(morningstarCode) 
            ? throw new ArgumentException($"A fund with Morningstar code {morningstarCode} already exists in the portfolio.", nameof(morningstarCode)) : "";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() {
            Headless = true // Set to false to see the browser window
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        _ = await page.GotoAsync(url: $"https://global.morningstar.com/it/investimenti/fondi/{morningstarCode}/grafico");

        var Name = await page.Locator("//span[@itemprop='name']").TextContentAsync() 
            ?? throw new Exception("Could not find Fund name element.");
        var ISIN = await page.Locator("//abbr[@class='investments-page__title-identifier__mdc']").TextContentAsync() 
            ?? throw new Exception("Could not find Fund ISIN element.");
        var Currency = await page.Locator("//button[@aria-label='Currency']").TextContentAsync() 
            ?? throw new Exception("Could not find Fund currency element.");

        Fund fund = new()
        {
            Name = Name.Trim(),
            ISIN = ISIN.Trim(),
            Currency = Currency.Trim(),
            NavQuotes = new List<Fund.NavQuote>()
        };
        Funds[morningstarCode] = fund;
        Save();
        Console.WriteLine($"Added fund '{fund.Name}' with ISIN '{fund.ISIN}' and currency '{fund.Currency}' to portfolio '{FilePath}'.");
    }
    
    public void List()
    {
        foreach (var fund in Funds)
        {
            Console.WriteLine($"Fund: {fund.Value.Name}, ISIN: {fund.Value.ISIN}, Currency: {fund.Value.Currency}");
            foreach (var navQuote in fund.Value.NavQuotes)
            {
                Console.WriteLine($"  NAV: {navQuote.Value}, Change: {navQuote.ChangePercent} %, Date: {navQuote.Date}");
            }
        }
    }

    public async Task Update()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() {
            Headless = true // Set to false to see the browser window
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        foreach (var fundEntry in Funds)
        {
            var morningstarCode = fundEntry.Key;
            var fund = fundEntry.Value;
            _ = await page.GotoAsync(url: $"https://global.morningstar.com/it/investimenti/fondi/{morningstarCode}/grafico");

            var date = await page.GetByText("Data di fine Al").TextContentAsync() 
                ?? throw new Exception("Could not find NAV quote date element.");
            var dateParsed = ParseDate(date);
            if (fund.NavQuotes.Count > 0 && fund.NavQuotes[^1].Date == dateParsed)
            {
                Console.WriteLine($"NAV quote for fund '{fund.Name}' is already up to date. Skipping.");
                continue;
            }
            _ = await page.GotoAsync(url: $"https://global.morningstar.com/it/investimenti/fondi/{morningstarCode}/quote");
            var navValue = await page.Locator("//p[@class='sal-dp-value']").First.TextContentAsync() 
                ?? throw new Exception("Could not find NAV quote value element.");
            navValue = navValue.Replace(",", ".").Trim(); // Replace comma with dot for decimal parsing and trim whitespace
            var changePercent = await page.Locator("//p[@class='sal-dp-value']").Nth(1).TextContentAsync() 
                ?? throw new Exception("Could not find NAV quote change percentage element.");
            changePercent = changePercent.Replace(",", ".").Trim().TrimEnd('%'); // Replace comma with dot for decimal parsing and trim whitespace
            
            _ = !decimal.TryParse(navValue, out decimal navValueParsed) 
                ? throw new Exception($"Failed to parse NAV quote value for fund '{fund.Name}'. Value: '{navValue}'") : "";
            _ = !decimal.TryParse(changePercent, out decimal changePercentParsed) 
                ? throw new Exception($"Failed to parse NAV quote change percentage for fund '{fund.Name}'. Change%: '{changePercent}'") : "";
            Console.WriteLine($"Adding NAV quote for fund '{fund.Name}': Value: {navValueParsed}, Change: {changePercentParsed} %, Date: {dateParsed}");
            fund.NavQuotes.Add(new Fund.NavQuote(navValueParsed, changePercentParsed, dateParsed));
        }
        Save();
        Console.WriteLine($"Portfolio '{FilePath}' update completed.");
    }

    DateOnly ParseDate(string date)
    {
        date = date.Replace("Data di fine Al", "").Trim();
        string[] dateSplit = date.Split(' ');
        if ("gennaio".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "01";
        }
        else if ("febbraio".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "02";
        }
        else if ("marzo".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "03";
        }
        else if ("aprile".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "04";
        }
        else if ("maggio".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "05";
        }
        else if ("giugno".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "06";
        }
        else if ("luglio".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "07";
        }
        else if ("agosto".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "08";
        }
        else if ("settembre".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "09";
        }
        else if ("ottobre".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "10";
        }
        else if ("novembre".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "11";
        }
        else if ("dicembre".Contains(dateSplit[1], StringComparison.InvariantCultureIgnoreCase))
        {
            dateSplit[1] = "12";
        }
        else
        {
            throw new Exception($"Failed to parse month from NAV quote date. Date: '{date}'");
        }
        return DateOnly.FromDateTime(new DateTime(int.Parse(dateSplit[2]), int.Parse(dateSplit[1]), int.Parse(dateSplit[0])));
    }
}