# MatTrakr

Simple, plakative Tracking-App für **Movies, Series und Books** — was habe ich
gesehen/gelesen, was noch nicht?

- Eigene Listen pro Benutzer (Movies/Series/Books werden automatisch angelegt)
- Echtzeit-Suche mit Covern: TMDb (Movies/Series), Google Books mit Open-Library-Fallback (Books)
- Status Gesehen/Ungesehen bzw. Gelesen/Ungelesen, Filter & Sortierung
- Verschieben zwischen Listen (nur gleicher Typ)
- Teilen: mit registrierten Benutzern (Nur Lesen / Bearbeiten) oder per öffentlichem Read-Only-Link
- Rollen: Admin, Verwalter, User — Admin kann Verwaltern fremde Listen zuweisen
- Lokale Anmeldung, DB-gestützte Sessions (überleben Container-Restarts)

## Stack

ASP.NET Core Razor Pages (.NET 10) · EF Core + SQLite · Docker

## Starten

```powershell
# Development
docker compose -f docker-compose.dev.yml up --build -d

# Release
docker compose -f docker-compose.release.yml up --build -d
```

App: http://localhost:9242 — Login: `admin` / `admin` (bzw. `MATTRAKR_ADMIN_PASSWORD`),
Passwortänderung wird beim ersten Login erzwungen.

Alle Daten (SQLite-DB + Config) liegen im Volume unter `/app/data`.

## TMDb API-Key (für Movie-/Serien-Suche erforderlich)

Kostenlos unter https://www.themoviedb.org/settings/api erstellen, dann entweder:

- Env-Var im Compose-File: `Tmdb__ApiKey=<key>`, oder
- im Volume: `/app/data/config/settings.json`:

```json
{
  "Tmdb": { "ApiKey": "<key>" }
}
```

Die Buch-Suche funktioniert ohne Key (optional `GoogleBooks:ApiKey` für höhere Quota).

## Build & Versionierung

```powershell
.\build.ps1                  # local  -> mattrakr:local-<builddate>
.\build.ps1 -Target dev      # dev    -> mattrakr:nightly-<buildnumber>-<builddate>
.\build.ps1 -Target release  # release-> mattrakr:<major>.<minor>.<buildnumber>-<builddate>
```

Buildnummer inkrementiert automatisch (`.buildnumber`), Major/Minor steht in `VERSION`.
Branches: `main` (Release), `dev` (Development).
