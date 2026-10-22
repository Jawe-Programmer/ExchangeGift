# Deploying Exchange Gift

Choose the option that fits you:

| Option | Best for | Cost | Effort |
| --- | --- | --- | --- |
| [A. Your own Windows PC + Cloudflare tunnel](#a-your-own-pc-with-a-cloudflare-tunnel) | A one-off event, a few days | Free | Low |
| [B. SmarterASP.NET (or any IIS host)](#b-smarteraspnet-and-other-iis-hosts) | A site that stays online without your PC | Paid shared hosting | Medium |
| [C. Azure App Service](#c-azure-app-service-with-github-actions) | Automatic deployment from GitHub | Paid plan (a free tier exists but is unsuitable) | Medium |

Every option needs the same three things:

1. The app **built for .NET 10** (or a host that runs .NET 10).
2. A **writable folder** for the SQLite database (`App_Data`, or the folder you set in `Storage__Directory`).
3. The **admin password** supplied as the `Admin__Password` setting (never in Git).

## Settings the app understands

| Setting (environment variable) | Meaning | Default |
| --- | --- | --- |
| `Admin__Password` | Password for the `Administrator` account, used **the first time** the site starts | none (no account is created) |
| `Admin__UserName` | Admin user name | `Administrator` |
| `Admin__ForcePasswordReset` | `true` resets the admin password to `Admin__Password` at startup (remove afterwards) | `false` |
| `Storage__Directory` | Folder for the database and cookie keys (must be writable, not public) | `<site>/App_Data` |
| `RateLimit__ParticipantPerMinute` | Requests per minute per visitor on participant pages | `60` |
| `RateLimit__LoginPerMinute` | Login attempts per minute per visitor | `10` |
| `ASPNETCORE_ENVIRONMENT` | Leave as `Production` | `Production` |

On Windows hosts a double underscore (`Admin__Password`) is the way to write `Admin:Password` in an environment variable.

## Publishing the app

```powershell
dotnet publish src/ExchangeGift.Web -c Release -o publish
```

Everything needed (pages, themes, music, fonts, `web.config`) is in the `publish` folder. Upload the **contents** of that
folder (not the folder itself) to your site's root.

If your host does not offer .NET 10, publish **self-contained** instead (larger upload, no runtime needed):

```powershell
dotnet publish src/ExchangeGift.Web -c Release -r win-x86 --self-contained true -o publish
```

Use `win-x64` if your host is 64-bit. The generated `web.config` then points at `ExchangeGift.Web.exe` automatically.

---

## A. Your own PC with a Cloudflare tunnel

Your PC runs the site and the database; a free temporary tunnel gives it a public `https` address.

1. Install the .NET 10 SDK and clone the repository.
2. Set the admin password once:
   ```powershell
   dotnet user-secrets set "Admin:Password" "YourStr0ngPassword" --project src/ExchangeGift.Web
   ```
3. Install the tunnel tool once: `winget install --id Cloudflare.cloudflared`, then open a new terminal.
4. Start everything: `.\scripts\start-all.ps1`
   (or two terminals: `.\scripts\start-site.ps1` and `.\scripts\start-tunnel.ps1`).
5. The tunnel prints an address like `https://random-words.trycloudflare.com`. Open `/admin/login` on **that** address,
   sign in, and on the Dashboard save it as the **Sharing address**.
6. Create the group, generate the draw and send the personal links.
7. Keep the PC, the site window and the tunnel window running during the event. Closing the tunnel stops public access.

The `trycloudflare.com` address changes every time the tunnel restarts. After a restart, save the new address and
re-send the links. For a permanent address use a named Cloudflare tunnel with your own domain.

---

## B. SmarterASP.NET and other IIS hosts

These steps are written for SmarterASP.NET but apply to any Windows/IIS shared host.

### B1. Before you start

* In the host's control panel, find the **ASP.NET / .NET Core version** setting and make sure **.NET 10** is available for
  your site. If it is not, use the *self-contained* publish shown above.
* Create a (sub)domain or use the free temporary address your host gives you.
* Turn on the host's **free SSL certificate** for the domain so cookies are marked secure.

### B2. Set the admin password

Pick **one** way:

* **Control panel setting (best):** add an environment variable `Admin__Password` if your panel offers it.
* **`web.config`:** open `src/ExchangeGift.Web/web.config` (or the copy inside `publish`) and uncomment this line, with your own password:
  ```xml
  <environmentVariable name="Admin__Password" value="YOUR-PASSWORD-HERE" />
  ```
  Start the site once, then remove the line again. **Never commit a real password to Git.**

### B3. Upload

1. `dotnet publish src/ExchangeGift.Web -c Release -o publish`
2. Upload the **contents** of `publish` over FTP (or Web Deploy) to the site's root folder. `web.config` and `ExchangeGift.Web.dll`
   must sit together in that folder.
3. Delete any default `index.html` / `iisstart.htm` the host put there.
4. Make sure the site folder (or `App_Data`) is **writable**. The first start creates `App_Data` with `exchangegift.db`.
   If your host does not allow writing there, create a writable folder and set `Storage__Directory` (uncomment the line in `web.config`).

### B4. First start

1. Open `https://your-domain/admin/login` and sign in as `Administrator`. (`/admin` works too; if you hit an error page, go to `/admin` and try again.)
2. In *Password*, change the password. On the Dashboard save your real **Sharing address** (`https://your-domain`).
3. Create a test group with a couple of names, generate the draw and open one personal link to check everything.
4. Refresh the File Manager: `App_Data/exchangegift.db` should now exist.

### B5. Updating later

Publish again and upload the new files over the old ones. **Do not delete `App_Data`**: your data lives there. Database
upgrades are applied automatically at the next start.

### B6. Troubleshooting a hosted site

| Symptom | Cause and fix |
| --- | --- |
| `500 - Internal server error` with no details | The app did not start. In `web.config` set `stdoutLogEnabled="true"`, create an empty `logs` folder next to it, reload the page, then read the newest file in `logs`. Set it back to `false` afterwards. |
| Log says the .NET 10 framework was not found | Enable .NET 10 in the control panel, or redeploy a *self-contained* publish. |
| Log says access to a path is denied | Give the site folder / `App_Data` write permission, or set `Storage__Directory` to a writable folder. |
| `App_Data` never appears | The site is not starting (see above). It is created by the app on first start. |
| The default host page appears instead of the app | Delete `index.html` / `iisstart.htm`; check `web.config` is in the site root. |
| Can sign in locally but not on the host | Each site has its own database and password. |
| HTTP Error 500.19 / 500.30 | `web.config` problem or startup failure: check the stdout log above. Keep `web.config` as shipped; extra IIS sections are often locked on shared hosting. |

---

## C. Azure App Service with GitHub Actions

Recommended settings: **Windows** App Service, **Basic (B1) or higher**, a single instance.
SQLite stores everything in one file, so do not scale out to several instances.

### C1. Create the web app

1. Azure Portal → **Create a resource → Web App**.
2. Publish: **Code**. Runtime stack: **.NET 10 (LTS)**. Operating system: **Windows**.
   (If .NET 10 is not listed for your region, choose the newest version offered and use the self-contained publish.)
3. Pricing plan: **Basic B1** or higher (the Free tier sleeps and is slow to start).
4. Create it and note the **app name**.

### C2. Configure it

Web App → **Settings → Environment variables → App settings**, add:

| Name | Value |
| --- | --- |
| `Admin__Password` | your admin password (e.g. `YourStr0ngPassword`) |
| `Storage__Directory` | `D:\home\data\exchangegift` (the `D:\home` drive is persistent storage and survives deployments) |

Also under **Settings → Configuration → General settings** turn on **HTTPS Only**.
Save and let the app restart.

### C3. Deploy from GitHub (automatic)

The repository already contains `.github/workflows/azure-webapp.yml`.

1. In the Azure Portal, open the Web App → **Overview → Get publish profile** (download the file).
   (If it is disabled: Web App → *Settings → Configuration → General settings* → enable **SCM Basic Auth Publishing Credentials** and **FTP**, then try again.)
2. In GitHub: repository → **Settings → Secrets and variables → Actions → New repository secret**:
   * Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
   * Value: the **entire contents** of the downloaded publish profile file.
3. Open `.github/workflows/azure-webapp.yml` and replace `your-azure-app-name` with your web app name
   (or add a repository variable `AZURE_WEBAPP_NAME`).
4. Push to the `main` branch (or run the workflow manually: *Actions → Deploy to Azure App Service → Run workflow*).
   The workflow builds, runs the tests, publishes and deploys.
5. Browse to `https://<app-name>.azurewebsites.net/admin/login` and sign in as `Administrator` (if you see an error, go to `/admin`).

### C4. Deploy manually (no GitHub Actions)

```powershell
dotnet publish src/ExchangeGift.Web -c Release -o publish
Compress-Archive -Path publish\* -DestinationPath publish.zip -Force
az webapp deploy --resource-group <resource-group> --name <app-name> --src-path publish.zip --type zip
```

(Requires the Azure CLI and `az login`.)

### C5. After deploying

1. Sign in, change the password (*Password*), and save the **Sharing address** (`https://<app-name>.azurewebsites.net` or your custom domain).
2. **Back up the database:** Web App → **Development Tools → Advanced Tools (Kudu) → Debug console → CMD**, open
   `D:\home\data\exchangegift` and download `exchangegift.db` (stop the app first for a consistent copy).
3. Custom domain: Web App → **Custom domains** → add your domain and a free managed certificate.

### C6. Azure troubleshooting

| Symptom | Fix |
| --- | --- |
| `HTTP Error 500.30 - ASP.NET Core app failed to start` | Web App → **Log stream** or Kudu `LogFiles`. Usually a missing/incorrect runtime or an unwritable `Storage__Directory`. |
| Data disappears after a deployment | `Storage__Directory` is not set to a path under `D:\home`. Set it as in step C2. |
| "Invalid username or password" on a new deployment | The account is created at the first start. If it was created with another password, add `Admin__ForcePasswordReset` = `true`, restart once, then delete that setting. |
| Workflow fails at the deploy step | Re-download the publish profile and update the `AZURE_WEBAPP_PUBLISH_PROFILE` secret; check the app name. |

---

## Security checklist before sharing links

- [ ] HTTPS is on (free certificate on your host, *HTTPS Only* on Azure).
- [ ] You changed the admin password in **Admin → Password**.
- [ ] `web.config` in your repository contains **no real password**; no publish profiles, `.db` files or `App_Data` are committed (the supplied `.gitignore` excludes them).
- [ ] The **Sharing address** matches your real site address.
- [ ] You generated the draw only after the participant list was final.
- [ ] You tested one personal link end to end before sending them all.
- [ ] You have a **backup** of `App_Data` / `exchangegift.db`.
