# The Google side: which console does what, and what a script can do instead

Chess needs two things from Google: a **Firebase project** (the cloud correspondence courier) and,
later, a **Play listing** (Chess.Droid, #59). Between them they scatter work across half a dozen
hosts that all look equally official, which is how a browser ends up with seventeen Google tabs open
and no way to tell which two matter.

This is the map. It is written after the fact — the project is already provisioned and running — so
it doubles as the record of what was done and the recipe for doing it again.

Companion: `scripts/google-audit.ps1` reports the live state of everything below and can create the
parts that are creatable. Run it before believing any of this is still true.

## The tab list, decoded

| Host | What it is | Needed? | Scriptable instead |
|---|---|---|---|
| `console.firebase.google.com` | The Firebase console for `chess-app-bce7a` | **Yes**, for four things | `firebase` CLI does everything else |
| `console.cloud.google.com` | The *same project* seen as a GCP project | **Yes**, for API-key restrictions and the billing check | `gcloud services api-keys`, `gcloud beta billing` |
| `play.google.com/console` | Play Console | **Yes**, and mostly by hand | `androidpublisher` v3, but only after the app exists |
| `search.google.com/search-console` | Site ownership verification | Only if the account ever converts to an organisation | HTML file or meta tag — the site is ours, so this is a deploy, not a console |
| `sharpastro.github.io` | The Pages site | Not a console — it is the candidate *organisation website* | already deployed from a repo |
| `accounts.google.com` | Sign-in | Transient | — |
| `developer.android.com`, `developers.google.com`, `docs.cloud.google.com`, `support.google.com`, `android-developers.googleblog.com` | Documentation | No | close them |
| `me.developers.google.com` | Google's developer-profile/badges page | No | — |
| `one.google.com` | Personal Google One storage | No | — |
| `login.live.com`, `login.microsoftonline.com`, `partner.microsoft.com` | Microsoft Partner Center — the **MSIX** half (#52) | Yes, but a different stack entirely | see `packaging/windows/msix/README.md` |

Two of these are the same project wearing different hats. A Firebase project **is** a Google Cloud
project; the Firebase console shows the Firebase-shaped subset (apps, database, rules, auth
providers) and the Cloud console shows everything else (API keys and their restrictions, enabled
APIs, billing). Neither is a superset of the other, which is the actual reason both tabs stay open.

## What this repository already automates

| Thing | Where | Trigger |
|---|---|---|
| Rules tested against the emulator | `rules` job, `.github/workflows/dotnet-desktop.yml` | every push and PR |
| Rules **deployed** | same job | push to `main`, `FIREBASE_TOKEN` |
| `FIREBASE_CONFIG` injected into the web build | `pages.yml` | deploy |
| `FIREBASE_CONFIG` injected into the Android bundle | `aab` job | push to `main` |
| Bundle built, 16 KB alignment checked, upload-signed | `aab` job + `packaging/android/check-aab.ps1` | push to `main` |
| Project, database instance, app registrations | `scripts/google-audit.ps1 -Provision` | by hand, once |

One-shot CLI commands worth knowing, because each replaces a console visit:

```bash
firebase projects:create chess-app-bce7a
firebase database:instances:create chess-app-bce7a-default-rtdb --location europe-west1
firebase apps:create WEB     chess                                    # mints the browser key
firebase apps:create ANDROID chess-android --package-name org.sebgod.chess   # mints the native key
firebase apps:sdkconfig WEB <appId>        # the config that becomes FIREBASE_CONFIG
firebase deploy --only database            # the rules, which CI does for you
```

An **app registration is what mints an API key**. There is no separate "create a key" step in the
Firebase console, which is why the second (native, unrestricted-by-referrer) key came from
`apps:create ANDROID` and not from a click — see `docs/correspondence-play.md`, *One key per
front-end*.

## Should this be Terraform?

**No** — and the reason is specific rather than a general aversion.

1. **The only thing that changes is the one thing Terraform cannot do.** Firebase's own Terraform
   page lists "Deploying Firebase Realtime Database Security Rules via Terraform" as *not yet
   supported* and points at "other tooling". The rules are the security boundary, they change
   whenever the schema does, and they are already tested-and-deployed from CI. Terraform would own
   everything except the part that moves.
2. **What is left is create-once and permanent.** One project, one database instance whose location
   *cannot be changed after provisioning*, two app registrations. Four resources, none of which will
   ever be edited. Terraform earns its keep on fleets and on environments you rebuild; this is a
   single hand-built thing that is never rebuilt.
3. **State needs a home, and the obvious home costs the safety model.** The conventional backend is
   a GCS bucket, which needs a billing account, which makes this a Blaze project — and Spark's
   refusal to bill *is* the safety model here (`docs/correspondence-play.md`, *Operational setup*).
   Local or committed state for a project that holds API keys is worse than no state.
4. **Everything already exists**, so adopting Terraform starts with four `terraform import`s to
   describe resources nobody will touch again.
5. **It would not touch Play at all.** There is no Terraform provider for the Play Console, and no
   API that creates an app.

Firebase's Terraform page also notes that several features need a Cloud Billing account attached —
including Firebase Authentication with GCIP, which is the resource (`google_identity_platform_config`)
that would otherwise let Terraform toggle the anonymous sign-in provider this app depends on.

**When the answer would flip:** a second project (a staging backend), or a sibling repo needing the
same shape. At that point the thing to grow is `scripts/google-audit.ps1 -Provision`, which already
creates all four resources, rather than to adopt a state file.

Sources: [Get started with Terraform and Firebase](https://firebase.google.com/docs/projects/terraform/get-started),
[`google_firebase_database_instance`](https://registry.terraform.io/providers/hashicorp/google/latest/docs/resources/firebase_database_instance),
[Google Play Developer API reference](https://developers.google.com/android-publisher/api-ref/rest).

## The irreducible clickops

Everything here needs a browser, and knowing *which* browser page saves the search.

**Firebase / Cloud console**

- **Enable the Anonymous sign-in provider.** Firebase console → Authentication → Sign-in method. No
  `firebase` command exposes it; the Identity Platform API that could is the Blaze-gated one above.
  Without it every cloud client fails at sign-in with a policy error rather than a bug.
- **Stay on Spark.** Not an action, an *inaction*: never accept the Upgrade prompt. The console is
  also the only place the plan is legible at a glance.
- **Usage and quota dashboards** (connections, storage, downloads) — readable nowhere else.
- API key restrictions are *not* on this list: `gcloud services api-keys list/update` manages the
  browser key's referrer restriction and the Android key's package+SHA-1 restriction. Only the
  reading of them is nicer in the console.

**Play Console** (all of #59, and the longest pole by far)

- **Create the app and claim `org.sebgod.chess`.** The publishing API has no `applications.create`:
  the resources are `edits`, `monetization`, `purchases`, `reviews`, `users` and an `applications`
  resource that only writes data-safety labels. The package name is permanent and unrecoverable.
- **Closed testing**, if the account is a personal one registered after 13 November 2023 — 12
  testers, 14 continuous days. Weeks of calendar time; start it first. (Organisation accounts are
  exempt, which is what the account-type question was about.)
- **Privacy policy URL**, **data safety** form, **content rating** questionnaire, **target audience**
  declaration, **store listing** assets.
- **Link a service account** (Users & permissions → invite the service account, grant release
  permissions). This is the one-time click that turns everything *after* it into API calls:
  uploading a bundle, moving it between tracks, managing testers.

So the honest shape of Play is: one long manual setup, then a scriptable release path.

## Drift found while writing this, and fixed the same day (2026-09-15)

`scripts/google-audit.ps1` found one real problem on its first run: **`FIREBASE_TOKEN` was not
set**, so the deploy step added on 2026-09-14 had never actually deployed. Every push to `main`
since had logged

```
##[notice]No FIREBASE_TOKEN secret. The rules were tested but not deployed.
```

which is the designed behaviour for a fork and exactly wrong for this repo — the live rules were
whatever was last deployed by hand. Two commands fixed it:

```bash
npx firebase login:ci          # browser consent, prints a token, no console visit
gh secret set FIREBASE_TOKEN   # reads stdin, echoes nothing
```

and the next push deployed for real:

```
✔  database: rules syntax for database chess-app-bce7a-default-rtdb is valid
✔  database: rules for database chess-app-bce7a-default-rtdb released successfully
```

That is the whole case for the audit script: the workflow said "deployed from CI", the secret list
said otherwise, and nothing in between was going to notice.

**Note that `login:ci` cannot run non-interactively** — it needs the consent screen, so it fails
with `Cannot run login:ci in non-interactive mode` from any wrapper, an agent session included. The
trap it sets is the obvious workaround: redirecting its output to a file parks an account-wide
refresh token in the working tree, untracked and, until 2026-09-15, unignored. `*.token` and
`*.secret` are in `.gitignore` for that reason.

## Running the audit

```powershell
pwsh scripts/google-audit.ps1              # read-only report
pwsh scripts/google-audit.ps1 -Provision   # create what is missing (safe to re-run)
pwsh scripts/google-audit.ps1 -Strict      # non-zero exit if anything required is missing
```

It never prints an API key, a database URL or a secret value — where a value has to be inspected
(the Android package name inside the SDK config) only the verdict is printed. `gcloud` is optional;
without it the billing-plan check reports UNKNOWN rather than guessing.
