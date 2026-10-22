# Signing releases with Microsoft Artifact Signing

Signed releases stop Windows SmartScreen's "Windows protected your PC" notice once the publisher builds a little
download reputation, and show your name as the publisher. The release workflow signs `KinahLedger.exe`, the installer
and every file the installer puts on the PC, as soon as the variables at the end of this page are set. Until then it
publishes unsigned releases, exactly as now.

Artifact Signing is Microsoft's signing service (it was called Trusted Signing until 2026). As an individual you need
to live in the United States or Canada. The Basic plan is about $9.99 a month for up to 5,000 signatures; a release
uses a few hundred at most.

Parts 1 to 3 need you (your Azure account and your ID). Part 4 is copy and paste. Allow a few days for Microsoft's
identity check in part 2.

## 1. Azure account and the signing account (about 10 minutes)

1. Sign in at [portal.azure.com](https://portal.azure.com) with the Microsoft account you want to own this. If it has
   no subscription, create a **Pay-As-You-Go** subscription (a card is needed; nothing is charged until you create
   something).
2. In the subscription: **Settings › Resource providers**, find **Microsoft.CodeSigning**, **Register**.
3. Search the portal for **Artifact Signing** (it may still show as **Trusted Signing Accounts**) › **Create**:
   - Resource group: new, `kinah-ledger-signing`
   - Account name: `kinahledger` (letters and numbers only; this is `AZURE_SIGNING_ACCOUNT`)
   - Region: **East US** (its endpoint is `https://eus.codesigning.azure.net`, this is `AZURE_SIGNING_ENDPOINT`)
   - Pricing tier: **Basic**
4. Open the new account › **Access control (IAM)** › **Add role assignment** › **Artifact Signing Identity Verifier**
   (or **Trusted Signing Identity Verifier**) › assign it to **yourself**. Without it, the next part's buttons are grey.

## 2. Identity validation (a few minutes, then 1 to 7 days)

1. In the account: **Identity validations › New identity › Public › Individual**.
2. Enter your legal name and address exactly as on your ID. Microsoft's partner then asks for a photo of a government
   ID and a selfie, on your phone.
3. Wait for **Completed**. Microsoft emails if it needs anything.

## 3. Certificate profile (2 minutes, after validation)

1. In the account: **Certificate profiles › Create › Public Trust**.
2. Name: `kinah-ledger` (this is `AZURE_SIGNING_PROFILE`). Pick the identity validated in part 2.

## 4. Let the GitHub release workflow sign (10 minutes)

The workflow signs in to Azure with GitHub's own short-lived token (OpenID Connect): no password or key is stored in
the repository, and Azure accepts only runs of the `release` environment of `StevenMacchia/kinah-ledger`.

1. Portal › **Microsoft Entra ID › App registrations › New registration**: name `kinah-ledger-release`, single
   tenant › **Register**. Note the **Application (client) ID** (`AZURE_CLIENT_ID`) and **Directory (tenant) ID**
   (`AZURE_TENANT_ID`).
2. In that app: **Certificates & secrets › Federated credentials › Add credential › GitHub Actions deploying Azure
   resources**:
   - Organization `StevenMacchia`, Repository `kinah-ledger`
   - Entity type **Environment**, Environment name `release`
   - Name `kinah-ledger-release` › **Add**
3. Back on the signing account (part 1) › **Access control (IAM) › Add role assignment › Artifact Signing Certificate
   Profile Signer** (or **Trusted Signing Certificate Profile Signer**) › assign it to the app `kinah-ledger-release`.
4. Your subscription's **Subscription ID** (portal › Subscriptions) is `AZURE_SUBSCRIPTION_ID`.
5. GitHub › the repository › **Settings › Environments › New environment** `release`:
   - **Deployment branches and tags › Selected › Add rule**: tag pattern `v*.*.*` (so only version tags can sign)
   - **Environment variables**, one each:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | from step 1 |
| `AZURE_TENANT_ID` | from step 1 |
| `AZURE_SUBSCRIPTION_ID` | from step 4 |
| `AZURE_SIGNING_ENDPOINT` | `https://eus.codesigning.azure.net` (or your region's) |
| `AZURE_SIGNING_ACCOUNT` | `kinahledger` |
| `AZURE_SIGNING_PROFILE` | `kinah-ledger` |

None of these is a secret. The next version tag builds a signed release; its Properties › Digital Signatures tab shows
your name.

## If a signed build fails

The run log names the step. "AADSTS70021" or "no matching federated identity" means the federated credential's
environment name isn't exactly `release`. "403" while signing means the app lacks the Certificate Profile Signer role.
Remove `AZURE_SIGNING_ACCOUNT` from the environment to go back to unsigned releases at once.
