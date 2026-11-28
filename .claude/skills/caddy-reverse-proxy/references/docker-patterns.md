# Docker Integration Patterns

## Basic Docker Compose

```yaml
services:
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"  # HTTP/3
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    networks:
      - web

volumes:
  caddy_data:    # Certificates - MUST persist
  caddy_config:  # Config cache

networks:
  web:
    driver: bridge
```

**Critical**: Always persist `/data` volume for certificates.

## Proxy to Other Containers

### Same Docker Network

```yaml
# docker-compose.yml
services:
  caddy:
    image: caddy:2-alpine
    networks:
      - web
    # ...

  app:
    image: myapp:latest
    networks:
      - web
    expose:
      - "8080"  # No port mapping needed
```

```
# Caddyfile
example.com {
    reverse_proxy app:8080
}
```

Use container/service name as hostname.

### Multiple Networks

```yaml
services:
  caddy:
    networks:
      - web
      - internal

  frontend:
    networks:
      - web

  api:
    networks:
      - web
      - internal

  database:
    networks:
      - internal  # Not accessible from caddy
```

## Proxy to Host Services

For services running on the Docker host (not in containers):

```yaml
services:
  caddy:
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

```
# Caddyfile
example.com {
    reverse_proxy host.docker.internal:8080
}
```

### Linux Alternative

On older Docker versions without `host-gateway`:

```yaml
extra_hosts:
  - "host.docker.internal:172.17.0.1"
```

Or use host network mode (loses container isolation):

```yaml
services:
  caddy:
    network_mode: host
```

## Firewall Considerations (UFW)

Docker bypasses UFW by default. For host services:

```bash
# Allow Docker networks to reach host port
sudo ufw allow from 172.16.0.0/12 to any port 8080
```

## Custom Caddy Image with DNS Plugin

```dockerfile
FROM caddy:2-builder AS builder
RUN xcaddy build \
    --with github.com/caddy-dns/cloudflare

FROM caddy:2-alpine
COPY --from=builder /usr/bin/caddy /usr/bin/caddy
```

```yaml
services:
  caddy:
    build: ./caddy
    environment:
      - CF_API_TOKEN=${CF_API_TOKEN}
```

## Environment Variables in Caddyfile

```
# Caddyfile
{$DOMAIN} {
    reverse_proxy {$BACKEND}:{$PORT:8080}
    tls {
        dns cloudflare {$CF_API_TOKEN}
    }
}
```

```yaml
services:
  caddy:
    environment:
      - DOMAIN=example.com
      - BACKEND=app
      - PORT=3000
      - CF_API_TOKEN=${CF_API_TOKEN}
```

## Health Checks

```yaml
services:
  caddy:
    healthcheck:
      test: ["CMD", "caddy", "version"]
      interval: 30s
      timeout: 10s
      retries: 3
```

## Resource Limits

```yaml
services:
  caddy:
    deploy:
      resources:
        limits:
          cpus: '0.5'
          memory: 256M
        reservations:
          memory: 128M
```

## Reload Configuration

```bash
# Graceful reload (zero downtime)
docker exec caddy caddy reload --config /etc/caddy/Caddyfile

# Or via API
docker exec caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
```

## Complete Multi-Service Example

```yaml
version: "3.8"

services:
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    networks:
      - web
    extra_hosts:
      - "host.docker.internal:host-gateway"
    depends_on:
      - api
      - frontend

  frontend:
    image: nginx:alpine
    networks:
      - web
    expose:
      - "80"

  api:
    image: node:20-alpine
    networks:
      - web
      - internal
    expose:
      - "3000"
    environment:
      - DATABASE_URL=postgres://db:5432/app

  db:
    image: postgres:16-alpine
    networks:
      - internal
    volumes:
      - db_data:/var/lib/postgresql/data

volumes:
  caddy_data:
  caddy_config:
  db_data:

networks:
  web:
  internal:
```

```
# Caddyfile
example.com {
    encode gzip

    handle /api/* {
        reverse_proxy api:3000
    }

    handle {
        reverse_proxy frontend:80
    }
}
```

## caddy-docker-proxy Plugin

Auto-generate Caddyfile from Docker labels:

```yaml
services:
  caddy:
    image: lucaslorentz/caddy-docker-proxy:ci-alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - caddy_data:/data
    networks:
      - web

  whoami:
    image: traefik/whoami
    networks:
      - web
    labels:
      caddy: whoami.example.com
      caddy.reverse_proxy: "{{upstreams 80}}"
```

## Certificate Sync to Host

Extract certificates for host services (e.g., Mox):

```bash
#!/bin/bash
DOMAIN="mail.example.com"
CADDY_DATA="/var/lib/docker/volumes/caddy_data/_data"
DEST="/etc/mox/certs"

cp "$CADDY_DATA/caddy/certificates/acme-v02.api.letsencrypt.org-directory/$DOMAIN/$DOMAIN.crt" "$DEST/"
cp "$CADDY_DATA/caddy/certificates/acme-v02.api.letsencrypt.org-directory/$DOMAIN/$DOMAIN.key" "$DEST/"
```

Run via cron or systemd timer for automatic sync.
