# TLS and Certificate Management

## Automatic HTTPS

Caddy automatically:
- Obtains certificates from Let's Encrypt
- Redirects HTTP → HTTPS
- Renews certificates before expiry
- Staples OCSP responses

**Requirements**:
- Domain DNS points to server
- Ports 80 and 443 accessible
- Data directory persistent and writable

## Certificate Challenges

### HTTP Challenge (Default)

Uses port 80 for validation. Works automatically.

### TLS-ALPN Challenge (Default)

Uses port 443 for validation. Works automatically.

### DNS Challenge (Required for Wildcards)

```
*.example.com {
    tls {
        dns cloudflare {env.CF_API_TOKEN}
    }
    # site config
}
```

## DNS Providers

Install Caddy with DNS plugin or use xcaddy to build:

```bash
xcaddy build --with github.com/caddy-dns/cloudflare
```

### Cloudflare

```
tls {
    dns cloudflare {env.CF_API_TOKEN}
}
```

Token needs: Zone:DNS:Edit permissions.

### Route53

```
tls {
    dns route53 {
        access_key_id {env.AWS_ACCESS_KEY_ID}
        secret_access_key {env.AWS_SECRET_ACCESS_KEY}
    }
}
```

### Other Providers

- `digitalocean`
- `duckdns`
- `godaddy`
- `namecheap`
- `gandi`
- `vultr`
- [Full list](https://github.com/caddy-dns)

## Wildcard Certificates

```
*.example.com {
    tls {
        dns cloudflare {env.CF_API_TOKEN}
    }

    @app1 host app1.example.com
    handle @app1 {
        reverse_proxy app1:8080
    }

    @app2 host app2.example.com
    handle @app2 {
        reverse_proxy app2:8080
    }
}
```

## Custom Certificates

### Certificate Files

```
example.com {
    tls /path/to/cert.pem /path/to/key.pem
}
```

### Internal/Self-Signed

```
example.com {
    tls internal
}
```

Uses Caddy's local CA. Trust it with `caddy trust`.

## On-Demand TLS

Obtain certificates at request time (for unknown domains):

```
{
    on_demand_tls {
        ask http://auth-service/check-domain
        # Returns 200 to allow, anything else to deny
    }
}

https:// {
    tls {
        on_demand
    }
    reverse_proxy backend:8080
}
```

**Critical**: Always configure `ask` endpoint to prevent abuse.

## TLS Options

```
example.com {
    tls {
        # Protocol versions
        protocols tls1.2 tls1.3

        # Cipher suites (TLS 1.2 only)
        ciphers TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384

        # ACME settings
        ca https://acme-staging-v02.api.letsencrypt.org/directory

        # Key type
        key_type p256  # ed25519, p256, p384, rsa2048, rsa4096
    }
}
```

## Client Authentication (mTLS)

```
example.com {
    tls {
        client_auth {
            mode require_and_verify
            trust_pool file /path/to/ca.pem
        }
    }
}
```

**Modes**:
- `request` - Request cert, don't verify
- `require` - Require cert, don't verify
- `verify_if_given` - Verify if provided
- `require_and_verify` - Require and verify (default with trust_pool)

## Global TLS Options

```
{
    email admin@example.com

    acme_ca https://acme.zerossl.com/v2/DV90
    acme_eab {
        key_id abc123
        mac_key xyz789
    }

    # Use staging for testing
    # acme_ca https://acme-staging-v02.api.letsencrypt.org/directory

    # Local certificates for all sites
    # local_certs
}
```

## Certificate Storage

Certificates stored in Caddy data directory:
- Linux: `~/.local/share/caddy/`
- Docker: `/data/`

**Critical**: Persist `/data` volume to avoid rate limits.

## Disabling Automatic HTTPS

### Per-site

```
http://example.com {
    # HTTP only
}
```

### Global

```
{
    auto_https off
}
```

### Disable redirects only

```
{
    auto_https disable_redirects
}
```

## Testing Configuration

Use Let's Encrypt staging to avoid rate limits:

```
{
    acme_ca https://acme-staging-v02.api.letsencrypt.org/directory
}
```

## Rate Limits

Let's Encrypt limits:
- 50 certificates per domain per week
- 5 duplicate certificates per week
- 300 new orders per account per 3 hours

Caddy handles this gracefully with backoff and fallback to ZeroSSL.
