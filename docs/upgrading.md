# Upgrading and Maintenance

## Upgrading to a New Version

Back up your data before upgrading (see [Backup](#backup)).

```bash
# From the repository root
git pull

cd deployables
docker compose pull
docker compose up -d
```

`docker compose pull` fetches the latest images. `docker compose up -d` re-creates containers that use a newer image. User data in the `app-data` volume is not affected.

## Pinning a Specific Version

To use a specific release instead of `latest`, edit `deployables/docker-compose.yaml` and change the image tags:

```yaml
# Before
image: hamidmayeli/quick-vless:latest

# After
image: hamidmayeli/quick-vless:1.2.0
```

Then apply:

```bash
cd deployables
docker compose up -d
```

Available versions are listed on [Docker Hub](https://hub.docker.com/r/hamidmayeli/quick-vless/tags).

## Rolling Back

1. Note the version you were running before the upgrade (check `docker compose ps` or the image digest).
2. Pin that version in `docker-compose.yaml` as shown above.
3. Run `docker compose up -d`.

If you also restored a data backup (see below), run `docker compose down` first, restore the volume, then `docker compose up -d`.

## Backup

### Data volume

All user accounts and usage history live in the `quick-vless_app-data` Docker volume.

```bash
cd deployables

docker run --rm \
  -v quick-vless_app-data:/data \
  -v "$(pwd)":/backup \
  ubuntu \
  tar czf /backup/easy-xray-data-$(date +%Y%m%d).tar.gz /data
```

The archive is written to `deployables/easy-xray-data-YYYYMMDD.tar.gz`. Move it off the server.

### Configuration files

The `.env` file and the patched `xray/config.json` contain your cryptographic keys. Back them up separately and store them securely.

```bash
tar czf ~/easy-xray-config-$(date +%Y%m%d).tar.gz \
  deployables/.env \
  deployables/xray/config.json
```

## Restore from Backup

```bash
cd deployables
docker compose down

# Wipe and restore the volume
docker run --rm \
  -v quick-vless_app-data:/data \
  -v "$(pwd)":/backup \
  ubuntu \
  sh -c "rm -rf /data/* && tar xzf /backup/easy-xray-data-YYYYMMDD.tar.gz -C /"

docker compose up -d
```

Replace `YYYYMMDD` with the date of the backup file you want to restore.

## Stopping and Starting Services

```bash
cd deployables

docker compose stop          # stop containers, keep them and their volumes
docker compose start         # start previously stopped containers
docker compose restart       # restart all containers
docker compose down          # stop and remove containers (volumes are preserved)
docker compose down -v       # DESTRUCTIVE: removes containers AND the data volume
```

> `docker compose down -v` permanently deletes all user data, usage history, and admin accounts. There is no recovery without a backup.

## Viewing Logs

```bash
cd deployables

docker compose logs app              # management app logs (last 100 lines)
docker compose logs xray             # Xray logs
docker compose logs caddy            # Caddy (proxy + TLS) logs

docker compose logs -f app           # follow app logs in real time
docker compose logs --tail=200       # last 200 lines from all services
```

## Checking Running Containers

```bash
cd deployables
docker compose ps
```

All three services (`xray`, `app`, `caddy`) should show status `Up`.

## Disk Usage

Usage history CSVs accumulate over time. Each daily file is small (one row per user per day), so growth is negligible in normal operation. If you want to trim old data, delete files from the volume:

```bash
# Remove usage history older than 90 days
docker run --rm \
  -v quick-vless_app-data:/data \
  ubuntu \
  find /data/usage_history -name "*.csv" -mtime +90 -delete
```
