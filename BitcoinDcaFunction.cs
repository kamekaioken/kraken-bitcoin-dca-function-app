using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace BitcoinDcaApp;

public class BitcoinDcaFunction
{
    private static readonly HttpClient httpClient = new HttpClient();

    [FunctionName("BitcoinDcaFunction")]
    public static async Task Run([TimerTrigger("0 30 17 * * *")] TimerInfo myTimer, ILogger log)
    {
        log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

        string apiKey = Environment.GetEnvironmentVariable("KRAKEN_API_KEY", EnvironmentVariableTarget.Process);
        string apiSecret = Environment.GetEnvironmentVariable("KRAKEN_PRIVATE_KEY", EnvironmentVariableTarget.Process);
        string bitcoinAmount = Environment.GetEnvironmentVariable("BITCOIN_AMOUNT", EnvironmentVariableTarget.Process);
        if (!decimal.TryParse(Environment.GetEnvironmentVariable("PRICE_THRESHOLD", EnvironmentVariableTarget.Process), out decimal priceThreshold))
        {
            log.LogError("PRICE_THRESHOLD environment variable is not set or invalid.");
            return;
        }

        string tickerUrl = "https://api.kraken.com/0/public/Ticker?pair=XBTEUR";
        HttpResponseMessage tickerResponse = await httpClient.GetAsync(tickerUrl);
        if (!tickerResponse.IsSuccessStatusCode)
        {
            log.LogError("Failed to retrieve BTC price.");
            return;
        }

        var tickerResponseContent = await tickerResponse.Content.ReadAsStringAsync();
        var tickerData = JsonDocument.Parse(tickerResponseContent);
        var lastTradePrice = decimal.Parse(
            tickerData.RootElement.GetProperty("result").GetProperty("XXBTZEUR").GetProperty("c")[0].GetString(),
            System.Globalization.CultureInfo.InvariantCulture);

        if (lastTradePrice > priceThreshold)
        {
            log.LogInformation($"Price {lastTradePrice} is above threshold {priceThreshold}. No order placed.");
            return;
        }

        // Kauf durchführen, wenn der Preis unterhalb der Schwelle liegt
        string apiUrl = "https://api.kraken.com/0/private/AddOrder";
        string nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        string postData = $"nonce={nonce}&ordertype=market&pair=XXBTZEUR&type=buy&volume={bitcoinAmount}";
        string apiSign = CreateSignature("/0/private/AddOrder", postData, nonce, apiSecret);

        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("API-Key", apiKey);
        httpClient.DefaultRequestHeaders.Add("API-Sign", apiSign);

        var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await httpClient.PostAsync(apiUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            log.LogError($"Failed to place order: {response.StatusCode}");
            return;
        }

        // Deserialisieren der Antwort
        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        // Überprüfen Sie, ob die Antwort einen Fehler enthält
        if (doc.RootElement.TryGetProperty("error", out var error) && error.GetArrayLength() > 0)
        {
            // Fehlerbehandlung, wenn das "error"-Array Einträge enthält
            log.LogError($"Error in response: {error}");
            return;
        }

        // Extrahieren von nützlichen Informationen aus der Antwort
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
}
