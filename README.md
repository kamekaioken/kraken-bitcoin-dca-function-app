
# Kraken Bitcoin DCA Function

This Azure Function is designed to automate Dollar-Cost Averaging (DCA) for Bitcoin purchases through the Kraken cryptocurrency exchange. It checks the current price of Bitcoin against a predefined threshold and places an order if the price is below this threshold.

## What the Function Does

The KrakenBitcoinDcaFunction is triggered by a timer and performs the following actions:
1. Fetches the current Bitcoin price in a specified fiat currency (e.g., EUR) from Kraken's public API.
2. Compares the fetched price against a predefined price threshold.
3. If the current price is below the threshold, it places a market order to buy a specified amount of Bitcoin using Kraken's private API.
4. Logs the outcome of the operation, including success or error messages.

## Starting Locally

To run this function locally, you will need to install Azurite and the Azure Functions Core Tools.

### Installing Azurite and Azure Functions Core Tools

1. Install Azurite, a lightweight Azure Storage emulator, by running:
   ```
   npm install -g azurite
   ```
2. Install the Azure Functions Core Tools to run and deploy your functions:
   ```
   npm install -g azure-functions-core-tools@3 --unsafe-perm true
   ```

### Running the Function

1. Start Azurite with the following command:
   ```
   azurite --silent --location . --debug azurite-debug.log
   ```
2. Start the Azure Function locally using:
   ```
   func start --verbose
   ```

## Configuration

Before running the function, rename `local.settings.template.json` to `local.settings.json` and configure the following settings:

- `TIMER_SCHEDULE`: Cron expression defining when the function triggers (e.g., "0 8 6 * * *" to run at 06:08 every day).
- `KRAKEN_API_KEY`: Your Kraken API key for authentication.
- `KRAKEN_PRIVATE_KEY`: Your Kraken private key for signing requests.
- `BITCOIN_AMOUNT`: The amount of Bitcoin to purchase.
- `PRICE_THRESHOLD`: The price threshold in your payment currency. Orders will only be placed if the current price is below this threshold.
- `PAYMENT_CURRENCY`: The fiat currency used for the payment (e.g., EUR).

## Deploying to Azure

Instead of running the function locally, you can also deploy it to Azure to automate your Bitcoin purchases. Follow the Azure Functions deployment guide for instructions on deploying to Azure.

