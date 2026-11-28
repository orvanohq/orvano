# Caddy Troubleshooting

## Common Errors

### "no upstreams available"

**Symptom**: 502 Bad Gateway, logs show "no upstreams available"

**Causes**:
1. Backend not running
2. Wrong hostname/port
3. Network isolation (Docker)

**Solutions**:
```bash
# Check backend is running
docker ps
curl http://backend:8080/health

# Verify DNS resolution from Caddy container
docker exec caddy nslookup backend

# Ensure same Docker network
docker network inspect web
```

### "dial tcp: connection refused"

**Symptom**: Caddy can't connect to upstream

**Causes**:
1. Backend not listening on expected port
2. Firewall blocking connection
3. Backend bound to wrong interface

**Solutions**:
```bash
# Check what's listening
ss -tlnp | grep 8080
docker exec backend ss -tlnp

# Ensure backend binds to 0.0.0.0, not 127.0.0.1
# Wrong: app.listen(8080, '127.0.0.1')
# Right: app.listen(8080, '0.0.0.0')
```

### "TLS handshake error"

**Symptom**: SSL errors when proxying to HTTPS backend

**Solutions**:
```
# If backend uses self-signed cert
reverse_proxy https://backend:443 {
    transport http {
        tls_insecure_skip_verify  # Not recommended for production
    }
}

# Better: Trust specific CA
reverse_proxy https://backend:443 {
    transport http {
        tls_trust_pool file /path/to/ca.pem
        tls_server_name backend.internal
    }
}
```

### Certificate Not Obtained

**Symptom**: "unable to obtain certificate", using self-signed

**Causes**:
1. DNS not pointing to server
2. Ports 80/443 blocked
3. Rate limited

**Solutions**:
```bash
# Verify DNS
dig +short example.com
nslookup example.com

# Check ports are open
curl -v http://example.com
sudo ss -tlnp | grep -E ':80|:443'

# Check Caddy logs
docker logs caddy 2>&1 | grep -i "certificate\|acme\|error"

# Use staging CA for testing
# In global options:
{
    acme_ca https://acme-staging-v02.api.letsencrypt.org/directory
}
```

### "Host header mismatch" or 403 Forbidden

**Symptom**: Backend rejects requests

**Cause**: Backend validates Host header

**Solution**:
```
reverse_proxy backend:8080 {
    header_up Host localhost
    # or
    header_up Host backend.internal
}
```

### WebSocket Connection Fails

**Symptom**: WebSocket upgrade fails, connection drops

**Solutions**:
```
# Ensure headers passed through (default behavior)
# Check for interfering middleware

# For long-lived connections
reverse_proxy backend:8080 {
    flush_interval -1
}
```

### Slow Response / Timeout

**Symptom**: Requests timeout, especially for long operations

**Solutions**:
```
reverse_proxy backend:8080 {
    transport http {
        dial_timeout 10s
        response_header_timeout 120s
    }
}
```

## Diagnostic Commands

### Check Configuration

```bash
# Validate Caddyfile
caddy validate --config /etc/caddy/Caddyfile

# In Docker
docker exec caddy caddy validate --config /etc/caddy/Caddyfile

# Format Caddyfile
caddy fmt --overwrite /etc/caddy/Caddyfile
```

### View Logs

```bash
# Docker logs
docker logs -f caddy

# With timestamps
docker logs -f --timestamps caddy

# Filter errors
docker logs caddy 2>&1 | grep -i error
```

### Enable Debug Mode

```
{
    debug
}

example.com {
    # ...
}
```

### Check Certificate Status

```bash
# View certificate details
openssl s_client -connect example.com:443 -servername example.com < /dev/null 2>/dev/null | openssl x509 -noout -dates

# Check certificate chain
curl -vI https://example.com 2>&1 | grep -A 10 "SSL certificate"
```

### Test Connectivity

```bash
# From host
curl -v http://localhost:8080

# From Caddy container
docker exec caddy wget -O- http://backend:8080

# DNS resolution
docker exec caddy nslookup backend
```

### Admin API

```bash
# Check running config
curl localhost:2019/config/

# Reload config
curl localhost:2019/load \
    -H "Content-Type: text/caddyfile" \
    --data-binary @/etc/caddy/Caddyfile
```

## Docker-Specific Issues

### Container Name Not Resolving

**Cause**: Containers on different networks

**Solution**: Ensure Caddy and backend on same Docker network

### host.docker.internal Not Working

**Linux**: Add to docker-compose.yml:
```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

Older Docker: Use host IP directly (e.g., `172.17.0.1`)

### UFW Blocking Docker-to-Host

**Solution**:
```bash
sudo ufw allow from 172.16.0.0/12 to any port 8080
```

### Certificate Data Lost After Restart

**Cause**: `/data` volume not persisted

**Solution**: Always define named volume:
```yaml
volumes:
  - caddy_data:/data
```

## Performance Issues

### High Memory Usage

**Solutions**:
- Reduce idle connections: `keepalive_idle_conns 10`
- Limit concurrent connections: `max_conns_per_host 100`
- Add resource limits in Docker

### Slow TLS Handshakes

**Solutions**:
- Enable OCSP stapling (default)
- Use faster key type: `key_type p256`
- Enable session resumption (default)

## Recovery Procedures

### Reset Certificates

```bash
# Remove certificate data (will re-obtain)
docker exec caddy rm -rf /data/caddy/certificates
docker restart caddy
```

### Clear Configuration

```bash
docker exec caddy rm -rf /config/caddy
docker restart caddy
```

### Force Reload

```bash
docker exec caddy caddy reload --config /etc/caddy/Caddyfile --force
```

## Official Resources

| Topic | URL |
|-------|-----|
| Documentation | https://caddyserver.com/docs/ |
| Community | https://caddy.community/ |
| GitHub Issues | https://github.com/caddyserver/caddy/issues |
