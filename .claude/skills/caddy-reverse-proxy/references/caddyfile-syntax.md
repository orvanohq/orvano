# Caddyfile Syntax Reference

## Basic Structure

```
# Global options (must be first)
{
    email admin@example.com
    # other global options
}

# Site blocks
example.com {
    # directives
}

another.example.com {
    # directives
}
```

## Site Addresses

```
example.com                    # HTTPS automatic
http://example.com             # HTTP only, no redirect
:8080                          # All interfaces, port 8080
localhost                      # Local with self-signed cert
*.example.com                  # Wildcard (requires DNS challenge)
example.com, www.example.com   # Multiple domains
```

## Request Matchers

### Inline Path Matcher

```
reverse_proxy /api/* backend:8080
file_server /static/*
```

### Named Matchers

```
@api {
    path /api/*
    method GET POST
}
reverse_proxy @api backend:8080

# Single-line form
@websocket header Connection *Upgrade*
```

### Matcher Types

**path** - Match URL path:
```
@static path /css/* /js/* /images/*
@exact path /robots.txt
```

**path_regexp** - Regex match with captures:
```
@assets path_regexp static \.(css|js|png|jpg)$
rewrite @assets /static{path}
```

**host** - Match hostname:
```
@www host www.example.com
redir @www https://example.com{uri}
```

**method** - Match HTTP method:
```
@post method POST PUT PATCH
@readonly method GET HEAD OPTIONS
```

**header** - Match headers:
```
@upgrade header Connection *Upgrade*
@json header Content-Type application/json
@no_auth header !Authorization
```

**query** - Match query params:
```
@search query q=*
@debug query debug=true
```

**remote_ip** - Match client IP:
```
@local remote_ip 192.168.0.0/16 10.0.0.0/8
@blocked remote_ip 1.2.3.4
```

**expression** - CEL expression:
```
@complex expression `{method} == "POST" && {path}.startsWith("/api")`
```

### Combining Matchers

Multiple matchers in a block are AND'd:
```
@api_post {
    path /api/*
    method POST
}
```

Same matcher type is OR'd:
```
@static {
    path /css/* /js/*  # matches /css/* OR /js/*
}
```

## Handle Directives

### handle - Route without modifying

```
example.com {
    handle /api/* {
        reverse_proxy api:8080
    }
    handle {
        file_server
    }
}
```

Handles are mutually exclusive - first match wins.

### handle_path - Strip prefix

```
example.com {
    handle_path /api/* {
        reverse_proxy api:8080  # /api/users → /users
    }
}
```

### route - Ordered execution

```
example.com {
    route {
        # Executes in order, unlike handle
        header X-Custom "value"
        reverse_proxy backend:8080
    }
}
```

## Common Directives

### encode - Compression

```
example.com {
    encode gzip zstd
    file_server
}
```

### header - Set/modify headers

```
example.com {
    header {
        Strict-Transport-Security "max-age=31536000"
        X-Content-Type-Options nosniff
        X-Frame-Options DENY
        -Server  # Remove header
    }
}
```

### redir - Redirects

```
example.com {
    redir /old /new permanent     # 301
    redir /temp /new temporary    # 302
    redir https://other.com{uri}  # External
}

# www redirect
www.example.com {
    redir https://example.com{uri} permanent
}
```

### rewrite - Internal path rewrite

```
example.com {
    rewrite /old/* /new{uri}
    rewrite * /index.html  # SPA fallback
}
```

### try_files - File fallback

```
example.com {
    root * /srv
    try_files {path} /index.html  # SPA
    file_server
}
```

### basicauth - Basic authentication

```
example.com {
    basicauth /admin/* {
        admin $2a$14$hash...  # bcrypt hash
    }
    reverse_proxy admin:8080
}
```

Generate hash: `caddy hash-password`

### log - Access logging

```
example.com {
    log {
        output file /var/log/caddy/access.log
        format json
    }
}
```

## Snippets (Reusable Blocks)

```
(security_headers) {
    header {
        Strict-Transport-Security "max-age=31536000"
        X-Content-Type-Options nosniff
        X-Frame-Options SAMEORIGIN
    }
}

example.com {
    import security_headers
    reverse_proxy backend:8080
}
```

## Environment Variables

```
{$DOMAIN:example.com} {
    reverse_proxy {$BACKEND_HOST}:{$BACKEND_PORT:8080}
}
```

`{$VAR:default}` - Use default if VAR not set.

## Placeholders

| Placeholder | Description |
|-------------|-------------|
| `{host}` | Request Host header |
| `{path}` | Request path |
| `{uri}` | Full URI (path + query) |
| `{query}` | Query string |
| `{method}` | HTTP method |
| `{remote_host}` | Client IP |
| `{scheme}` | http or https |
| `{upstream_hostport}` | Upstream address |

## Directive Order

Caddy executes directives in a predefined order. Custom order:

```
{
    order my_directive before reverse_proxy
}
```

Default order (partial):
1. `root`
2. `header`
3. `rewrite`
4. `handle`/`handle_path`/`route`
5. `reverse_proxy`
6. `file_server`
