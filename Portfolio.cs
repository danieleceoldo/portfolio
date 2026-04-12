using Microsoft.Playwright;
using System.Text.Json;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// TODO:
/// - Add logging to file with log rotation to avoid losing important information in case of crashes and to have a history of operations performed by the application
/// - Add unit tests to ensure the correctness of the application and to facilitate future refactoring and addition of new features
/// - Add error handling and retry logic to the Add and Update methods to handle transient issues such as network errors or changes in the website structure that may cause element retrieval to fail
/// - Add .json file name validation to ensure that the portfolio name provided by the user is valid and does not contain characters that are not allowed in file names, which could cause issues when saving the portfolio data
/// </summary>
class Portfolio
{
    string FilePath { get; }
    Dictionary<string, Fund> Funds { get; set; } = [];
    
    public Portfolio(string name)
    {
        _ = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Portfolio name cannot be null or empty.", nameof(name)) : "";
        if (name.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase))
        {
            name = name.Substring(0, name.Length - ".json".Length); // Remove .json extension if provided by the user to avoid issues with file naming
        }
        FilePath = $"{name}.json";
    }

    public async Task LoadAsync()
    {
        if (File.Exists(FilePath))
        {
            Funds = JsonSerializer.Deserialize<Dictionary<string, Fund>>(await File.ReadAllTextAsync(FilePath)) ?? new Dictionary<string, Fund>();
            Console.WriteLine($"Loaded portfolio from file '{FilePath}' with {Funds.Count} funds.");
        }
        else
        {
            Console.WriteLine($"Created new portfolio with name '{FilePath}'.");
        }
    }

    public async Task AddAsync(string morningstarCode)
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
        await LoadPageWithRetries(page, $"https://global.morningstar.com/it/investimenti/fondi/{morningstarCode}/grafico");

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
        await Save();
        Console.WriteLine($"Added fund '{fund.Name}' with ISIN '{fund.ISIN}' and currency '{fund.Currency}' to portfolio '{FilePath}'.");
        await page.CloseAsync();
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

    public async Task UpdateAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() {
            Headless = false // Set to false to see the browser window
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("https://www.blackrock.com/it/consulenti/products/229355/blackrock-world-technology-e2-eur-fund");
        System.Threading.Thread.Sleep(generalTimeout); // Wait for the page to fully load to avoid issues with element retrieval due to slow loading times or dynamic content loading
        await page.GotoAsync("https://global.morningstar.com/it/investimenti/fondi/0P0000VHOM/grafico");
        System.Threading.Thread.Sleep(generalTimeout); // Wait for the page to fully load to avoid issues with element retrieval due to slow loading times or dynamic content loading

        foreach (var fund in Funds)
        {
            try
            {
                await UpdateFundNavQuote(page, fund);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating NAV quote for fund '{fund.Value.Name}': {ex.Message}");
            }
            
        }
        await Save();
        Console.WriteLine($"Portfolio '{FilePath}' update completed.");
        await page.CloseAsync();
    }

    async Task Save()
    {
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(Funds, new JsonSerializerOptions { WriteIndented = true }));
    }   

    async Task UpdateFundNavQuote(IPage page, KeyValuePair<string, Fund> fund)
    {
        await LoadPageWithRetries(page, $"https://global.morningstar.com/it/investimenti/fondi/{fund.Key}/grafico");
        var date = await page.GetByText("Data di fine Al").TextContentAsync() 
            ?? throw new Exception("Could not find NAV quote date element.");
        var dateParsed = ParseDate(date);
        
        await LoadPageWithRetries(page, $"https://global.morningstar.com/it/investimenti/fondi/{fund.Key}/quote");
        var navValue = await page.Locator("//p[@class='sal-dp-value']").First.TextContentAsync() 
            ?? throw new Exception("Could not find NAV quote value element.");
        navValue = navValue.Replace(",", ".").Trim(); // Replace comma with dot for decimal parsing and trim whitespace
        _ = !decimal.TryParse(navValue, out decimal navValueParsed) 
            ? throw new Exception($"Failed to parse NAV quote value for fund '{fund.Value.Name}'. Value: '{navValue}'") : "";
        var changePercent = await page.Locator("//p[@class='sal-dp-value']").Nth(1).TextContentAsync() 
            ?? throw new Exception("Could not find NAV quote change percentage element.");
        changePercent = changePercent.Replace(",", ".").Trim().TrimEnd('%'); // Replace comma with dot for decimal parsing and trim whitespace
        _ = !decimal.TryParse(changePercent, out decimal changePercentParsed) 
            ? throw new Exception($"Failed to parse NAV quote change percentage for fund '{fund.Value.Name}'. Change%: '{changePercent}'") : "";

        if (fund.Value.NavQuotes.Count > 0 && (fund.Value.NavQuotes[^1].Date == dateParsed
            || fund.Value.NavQuotes[^1].Value == navValueParsed
            || fund.Value.NavQuotes[^1].ChangePercent == changePercentParsed))
        {
            Console.WriteLine($"NAV quote for fund '{fund.Value.Name}' is already up to date. Skipping.");
            return;
        }

        Console.WriteLine($"Adding NAV quote for fund '{fund.Value.Name}': Value: {navValueParsed}, Change: {changePercentParsed} %, Date: {dateParsed}");
        fund.Value.NavQuotes.Add(new Fund.NavQuote(navValueParsed, changePercentParsed, dateParsed));
    }
    
    static async Task LoadPageWithRetries(IPage page, string url, int maxRetries = 3)
    {
        int attempt = 0;
        while (attempt < maxRetries)
        {
            Thread.Sleep(generalTimeout); // Wait before each attempt to avoid overwhelming the server with requests in case of transient issues
            try
            {
                var response = await page.GotoAsync(url: url, new() { Timeout = generalTimeout });
                if (response != null && response.Ok)
                {
                    Thread.Sleep(generalTimeout); // Wait for the page to fully load
                    return; // Page loaded successfully
                }
                else
                {
                    Console.WriteLine($"Failed to load page. Status: {response?.Status}. Attempt {attempt + 1} of {maxRetries}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while loading page: {ex.Message}. Attempt {attempt + 1} of {maxRetries}.");
            }
            attempt++;
        }
        throw new Exception($"Failed to load page after {maxRetries} attempts. URL: '{url}'");
    }

    static async Task SendMessageToTelegram(string message)
    {
        string botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? throw new Exception("Environment variable 'TELEGRAM_BOT_TOKEN' is not set.");
        string chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? throw new Exception("Environment variable 'TELEGRAM_CHAT_ID' is not set.");
        using var client = new HttpClient();
        
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var data = new
        {
            chat_id = chatId,
            text = message
        };

        var response = await client.PostAsJsonAsync(url, data);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.Write($"Failed to send Telegram message. Status: {response.StatusCode}, Response: {errorContent}");
        }
    }

    static DateOnly ParseDate(string date)
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

    readonly static int generalTimeout = 60000; // General timeout of 60 seconds for page load and element retrieval to avoid overwhelming the server
}