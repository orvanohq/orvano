# reverse_proxy Directive

## Basic Syntax

```
reverse_proxy [<matcher>] <upstreams...> {
    # options
}
```

## Upstream Formats

| Format | Example |
|--------|---------|
| Host:port | `localhost:8080` |
| HTTP URL | `http://backend:8080` |
| HTTPS URL | `https://secure.backend:443` |
| HTTP/2 cleartext | `h2c://grpc-server:9000` |
| Unix socket | `unix//var/run/app.sock` |
| Port range | `localhost:8001-8006` |

**Note**: Upstream addresses cannot contain paths or query strings.

## Load Balancing

```
reverse_proxy node1:80 node2:80 node3:80 {
    lb_policy round_robin
}
```

**Policies**:
- `random` (default) - Random selection
- `round_robin` - Sequential rotation
- `least_conn` - Fewest active connections
- `first` - First available (failover)
- `ip_hash` - Sticky by client IP
- `uri_hash` - Sticky by request URI
- `cookie <name>` - Sticky via cookie
- `header <field>` - Hash header value

### Retries

```
reverse_proxy backends:80 {
    lb_retries 3
    lb_try_duration 5s
    lb_try_interval 250ms
}
```

## Health Checks

### Active Health Checks

```
reverse_proxy node1:80 node2:80 {
    health_uri /healthz
    health_interval 30s
    health_timeout 5s
    health_status 200
    health_passes 2
    health_fails 3
}
```

### Passive Health Checks

```
reverse_proxy backends:80 {
    fail_duration 30s
    max_fails 3
    unhealthy_status 500 502 503
    unhealthy_latency 5s
}
```

## Header Manipulation

### Request Headers (header_up)

```
reverse_proxy backend:8080 {
    # Set header
    header_up Host {upstream_hostport}

    # Add header (prefix with +)
    header_up +X-Real-IP {remote_host}

    # Remove header (prefix with -)
    header_up -Authorization

    # Regex replacement
    header_up Cookie "session=([^;]+)" "session=redacted"
}
```

### Response Headers (header_down)

```
reverse_proxy backend:8080 {
    header_down -Server
    header_down +X-Proxy "Caddy"
}
```

### Default Headers (automatic)

Caddy automatically sets:
- `X-Forwarded-For` - Client IP
- `X-Forwarded-Proto` - Original scheme (http/https)
- `X-Forwarded-Host` - Original Host header

## Transport Options

### HTTPS to Backend

```
reverse_proxy https://backend:443 {
    transport http {
        tls
        tls_server_name backend.internal
        tls_insecure_skip_verify  # NOT recommended
    }
}
```

### Custom TLS Trust

```
reverse_proxy backend:443 {
    transport http {
        tls
        tls_trust_pool file /path/to/ca.pem
    }
}
```

### HTTP/2 Cleartext (gRPC)

```
reverse_proxy h2c://grpc-server:9000
```

Or explicitly:
```
reverse_proxy grpc-server:9000 {
    transport http {
        versions h2c
    }
}
```

### Timeouts

```
reverse_proxy backend:8080 {
    transport http {
        dial_timeout 5s
        response_header_timeout 30s
        keepalive 2m
        keepalive_idle_conns 10
    }
}
```

## WebSocket Support

WebSockets work automatically. For streaming/SSE:

```
reverse_proxy backend:8080 {
    flush_interval -1  # Disable buffering
}
```

## Response Handling

### Intercept Errors

```
reverse_proxy backend:8080 {
    @error status 500 502 503 504
    handle_response @error {
        root * /srv/errors
        rewrite * /{rp.status_code}.html
        file_server
    }
}
```

### Replace Status

```
reverse_proxy backend:8080 {
    @not_found status 404
    replace_status @not_found 200
}
```

## Dynamic Upstreams

### From DNS SRV

```
reverse_proxy {
    dynamic srv _http._tcp.backend.service.consul
}
```

### From DNS A/AAAA

```
reverse_proxy {
    dynamic a backend.service.consul 8080 {
        refresh 30s
    }
}
```

## Common Patterns

### API Gateway

```
api.example.com {
    reverse_proxy /users/* users-service:8080
    reverse_proxy /orders/* orders-service:8080
    reverse_proxy /auth/* auth-service:8080
}
```

### Strip Path Prefix

```
example.com {
    handle_path /api/* {
        reverse_proxy backend:8080
    }
}
```

Request to `/api/users` reaches backend as `/users`.

### Preserve Path

```
example.com {
    handle /api/* {
        reverse_proxy backend:8080
    }
}
```

Request to `/api/users` reaches backend as `/api/users`.

### Backend with Different Host

```
example.com {
    reverse_proxy backend.internal:8080 {
        header_up Host backend.internal
    }
}
```
