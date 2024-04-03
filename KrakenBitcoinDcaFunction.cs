using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace KrakenBitcoinDcaApp;

public class KrakenBitcoinDcaFunction
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static string _paymentCurrency;

    [FunctionName("KrakenBitcoinDcaFunction")]
    public static async Task Run([TimerTrigger("%TIMER_SCHEDULE%")] TimerInfo myTimer, ILogger log)
    {
        log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

        _paymentCurrency = Environment.GetEnvironmentVariable("PAYMENT_CURRENCY", EnvironmentVariableTarget.Process);
        if (!decimal.TryParse(Environment.GetEnvironmentVariable("PRICE_THRESHOLD", EnvironmentVariableTarget.Process), out decimal priceThreshold))
        {
            log.LogError("PRICE_THRESHOLD environment variable is not set or invalid.");
            return;
        }

        var tickerResponse = await GetCurrentBitcoinTickerData();
        if (!tickerResponse.IsSuccessStatusCode)
        {
            log.LogError($"Failed to retrieve BTC price for the pair XBT{_paymentCurrency}.");
            return;
        }

        var lastTradePrice = await ExtractLastTradePrice(tickerResponse);

        if (lastTradePrice > priceThreshold)
        {
            log.LogInformation($"Price {lastTradePrice} is above threshold {priceThreshold}. No order placed.");
            return;
        }

        var response = await PlaceOrder();

        if (!response.IsSuccessStatusCode)
        {
            log.LogError($"Failed to place order: {response.StatusCode}");
            return;
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        if (doc.RootElement.TryGetProperty("error", out var error) && error.GetArrayLength() > 0)
        {
            log.LogError($"Error in response: {error}");
            return;
        }

        LogSuccessResult(log, doc);
    }

    private static async Task<HttpResponseMessage> GetCurrentBitcoinTickerData()
    {
        var tickerUrl = $"https://api.kraken.com/0/public/Ticker?pair=XBT{_paymentCurrency}";
        var tickerResponse = await _httpClient.GetAsync(tickerUrl);
        return tickerResponse;
    }

    private static async Task<decimal> ExtractLastTradePrice(HttpResponseMessage tickerResponse)
    {
        var tickerResponseContent = await tickerResponse.Content.ReadAsStringAsync();
        var tickerData = JsonDocument.Parse(tickerResponseContent);
        var lastTradePrice = decimal.Parse(
            tickerData.RootElement.GetProperty("result").GetProperty($"XXBTZ{_paymentCurrency}").GetProperty("c")[0].GetString(),
            System.Globalization.CultureInfo.InvariantCulture);
        return lastTradePrice;
    }

    private static async Task<HttpResponseMessage> PlaceOrder()
    {
        var apiUrl = "https://api.kraken.com/0/private/AddOrder";
        var apiKey = Environment.GetEnvironmentVariable("KRAKEN_API_KEY", EnvironmentVariableTarget.Process);
        var apiSecret = Environment.GetEnvironmentVariable("KRAKEN_PRIVATE_KEY", EnvironmentVariableTarget.Process);
        var bitcoinAmount = Environment.GetEnvironmentVariable("BITCOIN_AMOUNT", EnvironmentVariableTarget.Process);
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var postData = $"nonce={nonce}&ordertype=market&pair=XXBTZ{_paymentCurrency}&type=buy&volume={bitcoinAmount}";
        var apiSign = CreateSignature("/0/private/AddOrder", postData, nonce, apiSecret);

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("API-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("API-Sign", apiSign);

        var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await _httpClient.PostAsync(apiUrl, content);
        return response;
    }

    private static string CreateSignature(string path, string postData, string nonce, string privateKey)
    {
        var secretBytes = Convert.FromBase64String(privateKey);
        var np = nonce + postData;
        var pathBytes = Encoding.UTF8.GetBytes(path);
        var hash256Bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(np));
        var z = new byte[pathBytes.Length + hash256Bytes.Length];
        pathBytes.CopyTo(z, 0);
        hash256Bytes.CopyTo(z, pathBytes.Length);
        var signatureBytes = new HMACSHA512(secretBytes).ComputeHash(z);
        return Convert.ToBase64String(signatureBytes);
    }

    private static void LogSuccessResult(ILogger log, JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("result", out var result))
        {
            var descr = result.GetProperty("descr").GetProperty("order").GetString();
            var txid = result.GetProperty("txid").EnumerateArray().FirstOrDefault().GetString();

            log.LogInformation($"Successfully placed order: {descr}");
            log.LogInformation($"Transaction ID: {txid}");
        }
        else
        {
            log.LogError("Unexpected response structure.");
        }
    }
}
