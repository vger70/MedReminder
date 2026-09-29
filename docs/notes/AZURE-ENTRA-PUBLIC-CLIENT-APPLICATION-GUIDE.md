# Azure Entra ID Public Client Application Guide

## Purpose

Authenticate users with Microsoft Entra ID without using a client secret or certificate. This approach is intended for desktop, mobile, and command-line applications acting as Public Clients.

## Prerequisites

Before configuring the application, ensure the following prerequisites are met:

- An active Microsoft Entra ID tenant.
- Permission to create or manage App Registrations.
- A registered application in Microsoft Entra ID.
- MSAL (Microsoft Authentication Library) available in the client application.
- A desktop, mobile, or CLI application that cannot securely store a client secret.
- Network connectivity to Microsoft authentication endpoints.

## When to Use a Public Client Application

Use a Public Client Application for:

- WPF applications
- WinForms applications
- .NET console applications
- Mobile applications
- CLI tools

Do not use this model when the application can securely protect credentials on a backend server.

## Public Client Configuration

1. Open Microsoft Entra Admin Center.
2. Navigate to **Microsoft Entra ID > App registrations**.
3. Select the target application.
4. Open **Authentication**.
5. Select **Add a platform**.
6. Choose **Mobile and desktop applications**.
7. Add a redirect URI such as:

```text
http://localhost
```

8. Under **Advanced settings**, enable:

```text
Allow public client flows = Yes
```

9. Save the configuration.

## Client Secret Requirements

For user authentication only:

- No client secret is required.
- No certificate is required.

Client secrets and certificates are normally reserved for Confidential Client Applications such as web applications, background services, daemons, and APIs.

## MSAL.NET Example

```csharp
var app = PublicClientApplicationBuilder
    .Create(clientId)
    .WithTenantId(tenantId)
    .WithRedirectUri("http://localhost")
    .Build();
```

Interactive sign-in:

```csharp
var result = await app
    .AcquireTokenInteractive(scopes)
    .ExecuteAsync();
```

## Recommended OAuth Permissions

### Authentication Only

```text
openid
profile
offline_access
```

Meaning:

- `openid` obtains an ID token.
- `profile` provides basic profile information.
- `offline_access` enables refresh tokens.

### Microsoft Graph User Profile Access

```text
User.Read
```

Recommended baseline configuration:

```text
openid
profile
offline_access
User.Read
```

## Additional Microsoft Graph Permissions

Groups:

```text
GroupMember.Read.All
```

Directory:

```text
Directory.Read.All
```

Mail:

```text
Mail.Read
```

Files:

```text
Files.Read
```

## Accessing a Custom API

1. Register the API in Microsoft Entra ID.
2. Expose a custom scope.
3. Add the scope as a delegated permission in the client application.

Example:

```text
api://<client-id-api>/access_as_user
```

## Operational Checklist

### Azure Configuration

- [ ] App Registration created.
- [ ] Mobile and desktop platform added.
- [ ] Redirect URI configured.
- [ ] Allow Public Client Flows enabled.
- [ ] Required API permissions added.
- [ ] Admin consent granted where required.

### Application Configuration

- [ ] MSAL library installed.
- [ ] Correct Client ID configured.
- [ ] Correct Tenant ID configured.
- [ ] Redirect URI matches Azure configuration.
- [ ] Interactive authentication tested.
- [ ] Token acquisition verified.

### Validation

- [ ] User sign-in successful.
- [ ] ID token received.
- [ ] Access token received.
- [ ] Token refresh validated.
- [ ] Microsoft Graph access validated (if applicable).
- [ ] Custom API access validated (if applicable).

## Recommended Minimal Configuration

Application Type:

```text
Public Client Application
```

Redirect URI:

```text
http://localhost
```

Authentication Settings:

```text
Allow public client flows = Yes
```

Permissions:

```text
openid
profile
offline_access
User.Read
```
