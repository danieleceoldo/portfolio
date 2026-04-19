using Microsoft.Playwright;
using System.Text.Json;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.RegularExpressions;

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
    
    public Portfolio(string portfolioName)
    {
        if (string.IsNullOrWhiteSpace(portfolioName))
        {
            throw new ArgumentException("Portfolio name cannot be null or empty.", nameof(portfolioName));
        }
        if (portfolioName.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase))
        {
            portfolioName = portfolioName.Substring(0, portfolioName.Length - ".json".Length); // Remove .json extension if provided by the user to avoid issues with file naming
        }
        FilePath = $"{portfolioName}.json";
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
            await Save(); // Create an empty portfolio file if it does not exist to avoid issues with file not found when trying to save the portfolio data later   
            Console.WriteLine($"Created new portfolio with name '{FilePath}'.");
        }
    }

    public async Task AddAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Fund URL cannot be null or empty.", nameof(url));
        }
        
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() {
            Headless = true // Set to false to see the browser window
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoadPageWithRetries(page, url);

        var Name = (await page.Locator("//span[@class='product-title-main']").First.TextContentAsync())?.Trim()
            ?? throw new Exception("Could not find Fund name element.");
        if (Funds.ContainsKey(Name))
        {
            throw new ArgumentException($"A fund with name {Name} already exists in the portfolio.", nameof(Name));
        }
        var ISIN = (await page.Locator("//div[@class='product-data-item col-isin ']").Locator("//div[@class='data']").TextContentAsync())?.Trim() 
            ?? throw new Exception("Could not find Fund ISIN element.");
        var Currency = (await page.Locator("//div[@class='product-data-item col-seriesBaseCurrencyCode ']").Locator("//div[@class='data']").TextContentAsync())?.Trim() 
            ?? throw new Exception("Could not find Fund currency element.");

        Fund fund = new()
        {
            Url = url,
            ISIN = ISIN,
            Currency = Currency,
            NavQuotes = new List<Fund.NavQuote>()
        };
        Funds[Name] = fund;
        await Save();
        Console.WriteLine($"Added fund '{Name}' with ISIN '{fund.ISIN}' and currency '{fund.Currency}' to portfolio '{FilePath}'.");
        await page.CloseAsync();
    }
    
    public void List()
    {
        Console.WriteLine($"Listing funds in portfolio '{FilePath}':");

        if (Funds.Count == 0)
        {
            Console.WriteLine("No funds in the portfolio.");
            return;
        }

        foreach (var fund in Funds)
        {
            Console.WriteLine($"Fund: {fund.Key}, ISIN: {fund.Value.ISIN}, Currency: {fund.Value.Currency}");
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
            Headless = true // Set to false to see the browser window
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        foreach (string fundName in Funds.Keys)
        {
            try
            {
                await UpdateFundNavQuote(page, fundName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating NAV quote for fund '{fundName}': {ex.Message}");
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

    async Task UpdateFundNavQuote(IPage page, string fundName)
    {
        var fund = Funds[fundName];
        await LoadPageWithRetries(page, fund.Url);
        var date = await page.Locator("//span[@class='header-nav-label navAmount']").TextContentAsync() 
            ?? throw new Exception("Could not find NAV date element.");
        date = date.Trim().Substring("NAV al ".Length); // Remove "Data di fine Al" prefix and trim whitespace to get the date string
        if (!DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateParsed))
        {
            throw new Exception($"Failed to parse NAV quote date for fund '{fundName}'. Date: '{date}'");
        }
        if (fund.NavQuotes.Count > 0 && fund.NavQuotes.Last().Date == dateParsed)
        {
            Console.WriteLine($"NAV quote for fund '{fundName}' is already up to date with date '{dateParsed.ToString("dd/MM/yyyy")}'. Skipping update.");
            return; // Skip update if the latest NAV quote in the portfolio already has the same date as the one retrieved from the website to avoid adding duplicate NAV quotes
        }
        
        var navValue = await page.Locator("//span[@class='header-nav-data']").First.TextContentAsync()
            ?? throw new Exception("Could not find NAV quote value element.");
        navValue = navValue.Trim().Substring(fund.Currency.Length).Replace(",", ".").Trim(); // Remove "NAV " prefix and trim whitespace to get the NAV value string
        var navChange = await page.Locator("//span[@class='header-nav-data']").Nth(1).TextContentAsync()
            ?? throw new Exception("Could not find NAV quote value element.");
        navChange = navChange.Replace("\n", "").Trim(); // Trim whitespace to get the NAV change string
        Match match = Regex.Match(navChange, @"\(([-]?\d+,\d+%)\)");
        if (!match.Success)
        {
            throw new Exception($"Failed to parse NAV quote change for fund '{fundName}'. Change: '{navChange}'");
        }
        navChange = match.Groups[1].Value.Replace(",", ".").Trim()[..^1]; // Replace comma with dot for decimal parsing and trim whitespace
        _ = !decimal.TryParse(navValue, out decimal navValueParsed) 
            ? throw new Exception($"Failed to parse NAV quote value for fund '{fundName}'. Value: '{navValue}'") : "";
        _ = !decimal.TryParse(navChange, out decimal navChangeParsed) 
            ? throw new Exception($"Failed to parse NAV quote change percentage for fund '{fundName}'. Change%: '{navChange}'") : "";

        Console.WriteLine($"Adding NAV quote for fund '{fundName}': Value: {navValueParsed}, Change: {navChangeParsed} %, Date: {dateParsed}");
        fund.NavQuotes.Add(new Fund.NavQuote(navValueParsed, navChangeParsed, dateParsed));
    }
    
    async Task LoadPageWithRetries(IPage page, string url, int maxRetries = 3)
    {
        int attempt = 0;
        while (attempt < maxRetries)
        {
            Thread.Sleep(generalTimeout); // Wait before each attempt to avoid overwhelming the server with requests 
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

    async Task SendMessageToTelegram(string message)
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

    readonly int generalTimeout = 60000; // General timeout of 60 seconds for page load and element retrieval to avoid overwhelming the server
}