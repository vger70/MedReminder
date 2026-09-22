You need to implement a donation/support feature in my Windows desktop application, developed in C# with .NET 10 and WinForms.

## Important constraints

The application currently does NOT have an online backend/server.

DO NOT create an ASP.NET Core backend and DO NOT introduce server infrastructure at this stage.

The solution must work entirely from the desktop application by using externally hosted payment/checkout pages provided by the payment providers.

The priorities are:

1. Simplicity
2. Security
3. No secrets stored in the client application
4. Minimal maintenance
5. An architecture that can evolve to use a backend in the future

## Payment providers

Initially implement two providers:

* Stripe
* PayPal

The architecture should allow additional providers such as Ko-fi or Buy Me a Coffee to be added later.

## Architecture

Create an abstraction similar to:

```csharp
public interface IDonationProvider
{
    string Name { get; }

    Task<DonationLaunchResult> CreateDonationAsync(
        decimal amount,
        CancellationToken cancellationToken = default);
}
```

However, because there is currently no backend, DO NOT use server-side API secrets to dynamically create transactions.

The initial implementation must use public payment URLs / hosted payment pages / Payment Links configured on the providers.

Conceptual flow:

```text
WinForms application
        │
        ├── Stripe → Stripe Payment Link / Checkout URL
        │
        └── PayPal → PayPal payment URL
                         │
                         ▼
                     Web Browser
                         │
                         ▼
                     Payment Provider
```

## Donation amounts

The donation screen should offer:

* €2
* €5
* €10
* €20
* Custom amount, if supported safely

IMPORTANT:

Do NOT invent a mechanism for dynamically modifying the amount through a URL if the provider does not officially support it.

If the provider uses fixed-amount Payment Links, correctly implement the configured links for the available donation amounts.

If a provider officially supports a custom amount without requiring secret credentials or a backend, it may be implemented.

Otherwise, temporarily limit the available amounts to the configured fixed values and clearly document this limitation.

## Configuration

Payment URLs must be configurable without recompiling the application.

For example:

```json
{
  "Donations": {
    "Enabled": true,
    "Currency": "EUR",
    "Stripe": {
      "Enabled": true,
      "PaymentLinks": {
        "2": "https://...",
        "5": "https://...",
        "10": "https://...",
        "20": "https://..."
      }
    },
    "PayPal": {
      "Enabled": true,
      "PaymentLinks": {
        "2": "https://...",
        "5": "https://...",
        "10": "https://...",
        "20": "https://..."
      }
    }
  }
}
```

Do NOT put API secrets, private keys, client secrets, passwords, access tokens, or other private credentials in the desktop application's configuration.

Public payment URLs are acceptable in the client configuration because they are not authentication secrets.

## Browser handling

When the user selects a provider:

1. Validate the selected donation amount.
2. Determine the corresponding configured payment URL.
3. Open the URL in the user's default Windows browser.
4. Show a message explaining that the payment will be completed in the browser.

Use the standard Windows mechanism, for example:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = paymentUrl,
    UseShellExecute = true
});
```

Handle exceptions appropriately.

DO NOT use the legacy `WebBrowser` control.

DO NOT embed payment pages directly inside the application.

## WinForms UI

Create a new Form or UserControl that matches the existing application's visual style.

Suggested title:

"❤️ Support Development"

Suggested description:

"Do you like this application? If you want, you can voluntarily support its development with a small contribution."

Donation amount section:

```text
Amount

[ €2 ] [ €5 ] [ €10 ] [ €20 ]
```

If supported by the provider/configuration:

```text
[ Custom amount: € ____ ]
```

Payment method section:

```text
Payment method

[ 💳 Stripe ]
[ PayPal ]
```

The final button should dynamically reflect the selected provider:

"Continue with Stripe"

or:

"Continue with PayPal"

The UI must clearly communicate that the contribution is voluntary and is not required to use the application.

## Payment status

Because there is no backend and the desktop application cannot reliably verify the actual payment result:

DO NOT claim that a donation was completed simply because the browser was opened.

After successfully opening the browser, show something such as:

"A payment page has been opened in your browser. Thank you for supporting the project!"

Optionally:

"You can return to the application when you're finished."

Do NOT attempt to verify the payment by reading the browser URL or relying on unauthenticated client-side information.

## Security

Security is critical.

The application MUST NOT contain:

* Stripe Secret Key
* Stripe restricted/private API keys
* PayPal Client Secret
* PayPal Access Tokens
* passwords
* private keys
* API credentials

The user must complete the payment directly on the provider's official hosted payment page.

The desktop application should only know the public URLs required to initiate the checkout.

## Configuration model

Create a configuration model similar to:

```csharp
public sealed class DonationOptions
{
    public bool Enabled { get; set; }

    public string Currency { get; set; } = "EUR";

    public StripeDonationOptions Stripe { get; set; } = new();

    public PayPalDonationOptions PayPal { get; set; } = new();
}
```

Adapt this to the configuration system already used by the project.

If the project does not currently have a configuration system, use the simplest appropriate approach consistent with the existing architecture.

## Provider abstraction

Create an extensible provider abstraction.

For example:

```csharp
public enum DonationProvider
{
    Stripe,
    PayPal,
    KoFi,
    BuyMeACoffee
}
```

The architecture should make it possible to add Ko-fi or Buy Me a Coffee later without having to rewrite the main UI/ViewModel.

Do NOT implement Ko-fi or Buy Me a Coffee at this stage.

## Logging

Use the logging infrastructure already present in the project.

Log:

* selected provider;
* donation amount;
* attempt to open checkout;
* errors while opening the browser;
* relevant non-sensitive diagnostic information.

DO NOT log:

* payment card information;
* secrets;
* API credentials;
* access tokens;
* private keys;
* sensitive payment information.

## Error handling

Handle at least:

* donations disabled;
* invalid amount;
* missing payment URL;
* malformed payment URL;
* unsupported donation amount;
* browser launch failure;
* incomplete configuration.

User-facing errors should be clear and non-technical.

For example:

"Unable to open the support page. Please try again later."

Technical details should only be available through application logs.

## Important: do not fake functionality

Because this first version has no backend:

DO NOT implement:

* webhooks;
* server-side payment verification;
* PayPal API order capture;
* Stripe API calls requiring secret keys;
* automatic confirmation of completed payments;
* attempts to bypass provider security;
* attempts to scrape or inspect browser payment results.

The goal of version 1 is simply to provide users with a secure and convenient way to reach the official payment pages.

## Future architecture

The architecture should allow a future migration from:

```text
Version 1

WinForms
    ↓
Payment Link
    ↓
Stripe / PayPal
```

to:

```text
Version 2

WinForms
    ↓
Backend
    ↓
Stripe / PayPal API
    ↓
Webhook
    ↓
Verified payment status
```

The UI and provider abstraction should require minimal changes when this happens.

## Technical requirements

Target:

* .NET 10
* C#
* Windows
* WinForms

Before implementing anything:

1. Analyze the existing project structure.
2. Identify the actual .NET version and target framework.
3. Identify the existing WinForms architecture.
4. Identify whether dependency injection is already being used.
5. Identify the existing configuration system.
6. Identify the existing logging system.
7. Identify existing naming conventions.
8. Identify the existing UI/design conventions.
9. Reuse existing infrastructure whenever possible.
10. Avoid introducing unnecessary NuGet packages or frameworks.

Do not modify unrelated parts of the application.

## Implementation requirements

The implementation should ideally be separated into:

```text
Donations/
    DonationProvider.cs
    IDonationProvider.cs
    DonationOptions.cs
    DonationResult.cs
    StripeDonationProvider.cs
    PayPalDonationProvider.cs
    DonationService.cs
```

Adapt the actual folder/file structure to the existing project conventions.

The UI should communicate with the donation service rather than containing payment-provider-specific logic directly.

## Validation

Before opening a payment URL:

* verify that the donation feature is enabled;
* verify that the selected provider is enabled;
* verify that the selected amount is valid;
* verify that a payment URL exists for the selected amount;
* verify that the URL uses HTTPS;
* verify that the URL is syntactically valid.

Do not allow arbitrary URLs to be entered by the end user.

## Testing

Add unit tests for at least:

* valid donation amount;
* zero amount;
* negative amount;
* amount exceeding the configured maximum;
* unsupported amount;
* provider disabled;
* missing payment URL;
* malformed URL;
* non-HTTPS URL;
* successful browser launch;
* browser launch failure;
* incomplete configuration.

Do not perform real payments during automated tests.

Use mocked/fake providers where appropriate.

## User experience

The donation feature should be unobtrusive.

It should be accessible through something like:

"❤️ Support Development"

or:

"Support the Project"

Do not repeatedly nag the user.

Do not show donation dialogs automatically on every application launch.

The feature should feel like an optional way to support the developer.

## Localization

The application may currently be localized.

Follow the project's existing localization architecture.

At minimum, all newly introduced user-facing strings should NOT be hardcoded directly inside UI logic if the project already has a localization system.

The initial language should be consistent with the existing application language.

## Output

Before modifying the code:

1. Analyze the existing project.
2. Briefly describe the current architecture relevant to this feature.
3. Propose the implementation architecture.
4. List the files that will be created or modified.
5. Explain the Stripe/PayPal flow without a backend.
6. Implement the feature.
7. Add the relevant unit tests.
8. Explain exactly where the Stripe and PayPal Payment Links need to be configured.
9. Explain how to create/configure the required Payment Links on the providers.
10. Verify that no secrets or private credentials are included in the desktop application.

## Final security check

Before considering the implementation complete, inspect the code and configuration and explicitly verify:

* No Stripe Secret Key exists in the client.
* No PayPal Client Secret exists in the client.
* No access tokens exist in the client.
* No private keys exist in the client.
* Only public payment URLs are used.
* Payment completion is NOT falsely reported by the application.
* All payment pages are opened using HTTPS.
* No payment card data is handled by the application.

The implementation should be production-ready once valid Stripe and PayPal Payment Links have been configured.
