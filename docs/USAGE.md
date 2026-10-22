# Using Exchange Gift

This guide walks through a whole gift exchange from first sign-in to the day everyone has revealed.

## 1. Sign in

1. Open `https://your-site/admin/login` (locally: `http://localhost:5000/admin/login`). Opening `/admin` also works and sends you to the login page when you are signed out.
   **If you see an error, or you get lost, go to `/admin`** (add `/admin` after your site address) and start again from there.
2. User name: `Administrator`. Password: the one you configured (see the README or [DEPLOYMENT.md](DEPLOYMENT.md)).
3. First thing to do: **Password** in the top menu to choose your own password.

The top menu has **Dashboard**, **Groups**, **New group**, **Password** and **Credits**.

## 2. Set the sharing address (once)

On the **Dashboard**, *Sharing address* decides how copied links start.
Enter the public address of your site (for example `https://gifts.example.com`) and press *Save address*.
If you leave it empty, links use the address you are browsing with, which is fine on a real domain but wrong when you
are working on `localhost` or when a tunnel address changes.

## 3. Create a group

1. **New group** (or *Create exchange gift group* on the dashboard).
2. **Title**, for example `Family Christmas 2026`. The URL slug (`family-christmas-2026`) is generated; you may edit it.
   Slugs are lowercase letters, numbers and hyphens, must be unique, and cannot be words the application uses
   (`admin`, `api`, `themes` …).
3. **Theme**: click a card to choose it. *Open full preview* shows the theme with a sample name (nothing is recorded).
4. **Links stop working after** (optional): a date after which personal links close.
5. **Participants**: paste the names, one per line. Blank lines are ignored; duplicate names are rejected.
6. *Create group*. The group starts as a **Draft**.

The year in the title ("2026") appears in the themes; the rest of the title is used as the small heading in the Dubai theme.

## 4. Prepare the participants (draft only)

On the group page you can still:

* **Add** a participant, or **paste several names**.
* **Rename** a person (edit the field, *Save name*) or **Remove** them.
* Add a **Wish list** for each person (see below).

## 5. Wish lists

Each person has an expandable **★ Wish list** button. Enter up to three wishes, one per line (100 characters each) and
press *Save wish list*. You can edit wishes at any time, even after the draw.

When someone reveals who they are buying for, they also see **that person's** wish list. Nobody ever sees their own
list or anyone else's before the reveal.

## 6. Generate the draw

1. Press **Generate exchange gift draw** (needs at least two participants).
2. Confirm. The draw is created once, in one step, using a secure random method:
   nobody is assigned themselves, everyone gives to exactly one person and receives from exactly one person.
3. The group becomes **Draw ready** and every personal link goes live. Names are locked.

The draw is stored permanently. Reloading, restarting the site or opening a link again never changes it.

## 7. Share the personal links

Each person's card shows **Personal link** with a **Copy** button. **Copy all links** copies one `Name: link` line per
person, ready to paste into a message. Send each person only their own link.

The public group page (`/family-christmas-2026`) shows just the group title and a box for a code. It does not list people.
The home page (`/`) has the same kind of form for group name + code.

## 8. Follow the reveals

* Each person shows **Revealed** (with date and time) or **Not revealed yet**. The dashboard and group page show totals.
* **Assigned recipient** is hidden as `••••••••`. Press the eye button to show one person's recipient; press again to hide.
  The names are fetched only when you press the button and each eye works independently.

> Tip: do not look at the recipients unless you really have to (for example someone lost their link and you are
> checking). The point is that nobody knows, including you.

## 9. Change the theme

*Theme* section on the group page: choose another card and *Save theme*. Everyone sees the new design the next time they
open their link. The draw does not change.

## 10. Settings, reset, archive, delete

* **Group settings**: title, expiry date, and the URL slug (the slug can only change before the draw, so shared links keep working).
* **Reset draw**: deletes all assignments **and** reveal history and returns the group to draft. If anyone has already
  revealed, you must type the group's URL slug to confirm, because their results would become wrong.
* **Archive group**: closes every personal link (people see "This gift exchange has finished") but keeps the data. *Restore group* reopens it.
* **Delete group**: permanently removes the group, participants, draw and reveal history (type the slug to confirm).
* After the **expiry date** passes, links close automatically.

## What participants see

1. They open their link. A themed welcome screen appears with their name.
2. **Default Secret Santa Reveal**: tap the envelope in the snow → a card opens → brush the snow away with a finger or mouse (or press *Clear it for me*) → the name and wish list appear.
3. **Dubai Family Christmas 2026**: press and hold the gold button until the Burj Khalifa is lit → fireworks → the name appears on a card, followed by the wish list.
4. Opening the link again gives the same result. *Back to the snow* / *Replay the fireworks* only replay the animation.

Phones in reduced-motion mode get a calmer version without falling snow, confetti or fireworks.

## Backups

All data is in one SQLite file: `App_Data/exchangegift.db` (and the `keys` folder next to it).

* **Windows helper:** stop the site, then run `scripts\backup-database.ps1`. It writes a timestamped copy to `backups\`.
* **Manual:** stop the site and copy `App_Data` (or the folder in the `Storage__Directory` setting) somewhere safe.
* **Restore:** stop the site, put the backup file back as `exchangegift.db`, start the site.
* On a hosting service, download `App_Data` over FTP / Kudu before and after your event.

## Forgot the admin password

1. Set `Admin:Password` (user secrets or the `Admin__Password` setting) to a new password **and** set `Admin:ForcePasswordReset` (`Admin__ForcePasswordReset`) to `true`.
2. Start the site once. The log shows "Administrator password was reset".
3. Remove the reset setting again so the password is not reset on every start.

Locally:

```powershell
dotnet user-secrets set "Admin:Password" "NewStr0ngPassword" --project src/ExchangeGift.Web
dotnet user-secrets set "Admin:ForcePasswordReset" "true" --project src/ExchangeGift.Web
dotnet run --project src/ExchangeGift.Web      # start once, then stop it
dotnet user-secrets remove "Admin:ForcePasswordReset" --project src/ExchangeGift.Web
```

## Troubleshooting

| Problem | Fix |
| --- | --- |
| "Invalid username or password" | User name is `Administrator`. The account only takes the configured password the first time it is created; use the reset steps above. |
| "Too many failed attempts" | The account is locked for ten minutes after five wrong tries. Wait. |
| An error page, or you are lost | Go to `/admin` on your site (for example `https://your-site/admin`). To sign in directly use `/admin/login`. |
| Nothing happens at `/admin`, or a 500 error on a host | See *Troubleshooting* in [DEPLOYMENT.md](DEPLOYMENT.md). |
| "address already in use" when starting | Another copy of the site is running. Stop it, or choose a port: `dotnet run --project src/ExchangeGift.Web --urls http://localhost:5090`. |
| Copied links start with `localhost` | Set the *Sharing address* on the dashboard. |
| A participant says the link "isn't available" | Check they used the full link, that the draw is generated, and that the group is not archived or expired. |
| Slug rejected | It must be unique, lowercase letters/numbers/hyphens, and not a reserved word. |
