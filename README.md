# GelbooruBackup
Lightweight .NET 8 solution for backing up Gelbooru favorites as plain folder optionally synchronizing to a Szurubooru keeping tags/posts structure.

This repository contains a small sync service (`gelbooru-sync`) plus example deployment files for a full Szurubooru stack (server, client UI, Postgres, FlareSolverr). The sync service stores its working data in a local `LiteDB` file (`all_posts.litedb`) inside the data volume — the Szurubooru stack handles the backend (Postgres) and web UI.

## What the example Docker stack does
The provided `docker-compose.yml.example` runs the full stack used in examples:

- `server` — Szurubooru backend service (requires Postgres).
- `client` — Szurubooru client UI (web front-end).
- `sql` — Postgres database used by Szurubooru.
- `flaresolverr` — optional Cloudflare bypass service used by the sync client when needed.
- `gelbooru-sync` — this project's container that downloads Gelbooru favorites and writes metadata to LiteDB; it can also push tags/posts to the Szurubooru backend.

`gelbooru-sync` keeps a local database file (`all_posts.litedb`) in its mounted data volume. That file is independent from Szurubooru and is used by the sync tool to track posts, tags and sync metadata before optionally sending updates to Szurubooru if is not `SYNC_TO_SZURUBOORU=false` otherwise posts will be only save in folder as {id}.{ext}.

## docker-compose example (usage)
Example snippet (see `docker-compose.yml.example` in the repo):

```yaml
services:
  server:
    image: szurubooru/server:latest
    restart: unless-stopped
    depends_on:
      - sql
    environment:
      POSTGRES_HOST: sql
      THREADS: 6
    env_file:
      - szuruboorusync.env
    volumes:
      - ${DATA_PATH}/szurubooru/data:/data

  client:
    image: szurubooru/client:latest
    restart: unless-stopped
    depends_on:
      - server
    environment:
      BACKEND_HOST: server
      BASE_URL:
    env_file:
      - szuruboorusync.env
    volumes:
      - ${DATA_PATH}/szurubooru/data:/data:ro
    ports:
      - "4480:80"

  sql:
    image: postgres:11-alpine
    restart: unless-stopped
    env_file:
      - szuruboorusync.env
    volumes:
      - ${DATA_PATH}/szurubooru/pgdata:/var/lib/postgresql/data
    ports:
      - "45432:5432"
      
  gelbooru-sync:
    image: sharpsalat/gelboorusync:latest
    restart: unless-stopped
    depends_on:
      server:
        condition: service_started
    environment:
      BACKEND_HOST: server
    env_file:
      - szuruboorusync.env
    volumes:
      - ${DATA_PATH}/szurubooru/gelboorudata:/app/data
```
## .env example (usage)
Example snippet (see `szuruboorusync.env.example` in the repo):
```
# szurubooru
POSTGRES_USER: postgres
POSTGRES_PASSWORD: postgres2025

# gelbooru-sync required
GELBOORU_API_KEY=****************************************************************
GELBOORU_USER_ID=000000
GELBOORU_USERNAME=**********
GELBOORU_PASSWORD=**********
SZURUBOORU_USER_NAME=admin
SZURUBOORU_USER_PASSWORD=admin2025
DATA_PATH=gelbooru-sync

# gelbooru-sync optional
#FILES_FOLDER_PATH=/app/data
#BACKEND_HOST=http://{backendHost}:6666
#SHORT_SYNC_TIMEOUT=60
#FULL_SYNC_TIMEOUT=10800
#FULL_SYNC_ON_STARTUP=false
#SYNC_TO_SZURUBOORU=true   # set to false to disable syncing to Szurubooru
```
Notes:
- `DATA_PATH` is expected to be defined in your environment or an `.env` file and is used to persist Postgres and Szurubooru data plus the sync service data.
- The `gelbooru-sync` service mounts `${DATA_PATH}/szurubooru/gelboorudata` to `/app/data` in the container. The `all_posts.litedb` file will be created there.
- `flaresolverr` provides an HTTP API on port `8191`; the sync service will try to use it when Cloudflare blocks direct requests.

## Example env file
Create a `szuruboorusync.env` (example in repo). Important values:
- `DATA_PATH=/absolute/path/to/persist`
- `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` (for `sql` service)
- Szurubooru admin credentials (used by deployment scripts / initial setup)
- Gelbooru API credentials used by `gelbooru-sync` (if you prefer env-based config)

## Run the standard stack
1. Copy example files:
   - `cp docker-compose.yml.example docker-compose.yml`
   - `cp szuruboorusync.env.example szuruboorusync.env` and edit values.
2. Fill in `.env` required values:
   ```
   GELBOORU_API_KEY=****************************************************************
   GELBOORU_USER_ID=000000
   GELBOORU_USERNAME=**********
   GELBOORU_PASSWORD=**********
   ```
   - API key available at [Gelbooru account settings](https://gelbooru.com/index.php?page=account&s=options).
3. Start:
   - `docker compose up -d` (or `docker-compose up -d`).
4. Check logs:
   - `docker compose logs -f gelbooru-sync` to watch the sync progress.
5. Inspect data:
   - `all_posts.litedb` will live under `${DATA_PATH}/szurubooru/gelboorudata` on the host.
## Troubleshooting
- If `gelbooru-sync` cannot access Szurubooru, ensure `BACKEND_HOST` points to the `server` container and that the server has finished starting.
- If Cloudflare blocks requests, ensure `flaresolverr` is running and healthy (`/v1`).
- Verify volumes and `DATA_PATH` permissions so containers can write `all_posts.litedb` and Postgres data.
