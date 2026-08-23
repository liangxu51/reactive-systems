# API Gateway Design

**Date:** 2026-08-22
**Status:** Approved

## Problem

Clients reach the backend APIs through the frontend's nginx, which acts as a
rudimentary reverse proxy. This has accumulated three problems:

1. **Routing-table triplication.** The `/api/*` → service map is maintained in
   three places — `frontend/nginx.conf` (docker-compose),
   `k8s/helm/reactive-systems/templates/frontend-nginx-configmap.yaml`
   (Kubernetes), and `frontend/vite.config.js` (dev server) — with
   "keep both in sync" comments acknowledging the debt.
2. **Broken browser auth.** SEC-001 (PRs #72/#75) put HTTP Basic auth on every
   REST endpoint, but the frontend never sends an `Authorization` header — and
   the order stream uses `EventSource`, which *cannot* send custom headers. The
   UI's API calls all 401 today.
3. **Bypassable edge.** docker-compose publishes every backend port
   (8080/8081/8082) straight to the host, so nothing forces traffic through the
   proxy. Compose also sets no `SPRING_SECURITY_USER_*` vars, so the Java
   backends boot with a random password nothing knows.

## Decision

Introduce a dedicated **nginx API gateway** (`api-gateway/` module,
`nginx:alpine`) as the single API entry point, in both docker-compose and the
Helm chart.

Spring Cloud Gateway was considered and rejected: the repo has no Spring Cloud
dependency at all, and Boot 4.0.x would pin us to the Spring Cloud 2026.x
train — a heavy new dependency surface for a tutorial whose reactive concepts
are already illustrated by the existing services. Dedicated gateway products
(Kong, Envoy) are likewise off-theme and operationally heavy here.

## Architecture

```
browser ── frontend:80 (static + catch-all /api/ proxy)
                └── api-gateway:8080 ── order-service:8080   (/api/orders)
                                    └── inventory-service:8081 (/api/products)
```

- The browser keeps hitting `frontend:80` only — same-origin is preserved, so
  no CORS configuration is needed anywhere.
- The frontend nginx keeps serving static assets and proxies a single
  catch-all `location /api/` to the gateway. All per-route knowledge leaves
  the frontend layer.
- The gateway is published on host port **8080** in compose (the slot
  order-service vacates), so `curl localhost:8080/api/orders` keeps working
  for scripts and muscle memory — but now through the gateway.
- Kubernetes: the gateway is a normal Deployment + ClusterIP Service; the
  `frontend` Service stays the NodePort entry point. No Ingress (none exists
  today; adding one is out of scope for a minikube-targeted chart).
- shipping-service has no REST surface (Kafka-only) and is not routed.

### Single source of truth for routes

The entire routing table lives in **one file baked into the gateway image**:
`api-gateway/templates/default.conf.template`, rendered at container start by
the official nginx image's envsubst mechanism (`/etc/nginx/templates/` →
`/etc/nginx/conf.d/`). Environment differences are env vars, not config
copies:

| Env var | compose | Helm |
|---|---|---|
| `RESOLVER` | `127.0.0.11` (Docker DNS) | `{{ .Values.frontend.dnsResolverIP }}` |
| `ORDER_UPSTREAM` | `order-service:8080` (default) | `order-service.<ns>.svc.cluster.local:8080` |
| `INVENTORY_UPSTREAM` | `inventory-service:8081` (default) | `inventory-service.<ns>.svc.cluster.local:8081` |
| `API_AUTH_USERNAME/PASSWORD` | dev defaults, `.env`-overridable | `api-credentials` Secret |

Kubernetes runs the *same image* with different env — there is no configmap
copy of the routing table, so it cannot drift. Template discipline: `${VAR}`
is substituted at startup; `$var` stays an nginx runtime variable.

The frontend nginx conf and its Helm configmap remain two files, but each is
now a stable one-location catch-all that never changes when routes do.
The vite dev proxy collapses to a single `'/api' → http://localhost:8080`
rule pointing at the compose gateway — which also *fixes* dev-mode auth,
since bare backends 401 an unauthenticated browser.

### Order-service variants

Three interchangeable order-service implementations exist (Java, Java+virtual
threads, C#), selected by compose profile / Helm flag. The gateway handles
this via `ORDER_UPSTREAM`:

- compose default: `order-service:8080`; variant runs override it, e.g.
  `ORDER_UPSTREAM=order-service-vt:8083 docker compose --profile order-service-vt up`
  or `ORDER_UPSTREAM=order-service-cs:8080 docker compose --profile order-service-cs up`.
- Helm needs nothing: all three variants publish the same Service name
  `order-service` on port 8080.

(Bonus fix: the old frontend nginx hardcoded `order-service:8080`, so the UI
was broken under the vt/cs profiles in compose.)

## Edge auth: silent credential injection

The gateway holds the API's Basic credential and sets
`proxy_set_header Authorization "Basic ${API_AUTH_B64}"` on every proxied
route. The browser stays unauthenticated.

- Fixes the `EventSource` header gap with zero frontend or backend code
  change.
- Backends keep enforcing Basic auth (defense in depth): anything that reaches
  them *not* through the gateway still gets a 401.
- The base64 value is computed by an entrypoint drop-in
  (`docker-entrypoint.d/15-encode-basic-auth.sh`) before envsubst runs, since
  envsubst cannot compute.
- compose credential gap fixed by a `x-api-auth-env` anchor giving the Java
  services the same dev-default credential (`admin` /
  `reactive-systems-dev-api`) that order-service-cs already established,
  overridable via `.env`.

**Accepted trade-off:** the edge itself is open — anyone who can reach port
80/8080 can use the API. That matches the pre-SEC-001 exposure and is
appropriate for a tutorial demo. Documented extensions if real edge auth is
ever wanted: nginx `auth_basic` browser challenge (works with `EventSource`
because the browser auto-attaches credentials after the first challenge), or
cookie/session auth via an auth service.

## Cross-cutting concerns (deliberately minimal)

- **Rate limiting** — POST `/api/orders` only, keyed by client IP
  (5 r/s, burst 10, 429 on excess) via the `map $request_method` idiom.
  GET `/api/orders` is the SSE stream and must never be limited.
- **Access log** — one flat JSON line per request (`escape=json`) to stdout:
  timestamp, request_id, client, method, uri, status, request/upstream
  timings, bytes. Consistent with the backends' ECS JSON logging (#47) and
  Promtail-parseable without new pipeline work.
- **Request ID** — honor an incoming `X-Request-ID`, else generate from
  nginx's `$request_id`; forwarded upstream, echoed on the response, included
  in the access log. The gateway does **not** fabricate W3C `traceparent` —
  the backends generate proper trace context themselves, and open-source
  nginx cannot join that trace.
- **Timeouts** — connect 5s everywhere; read 30s on `/api/products`; read 1h
  + `proxy_buffering off` + HTTP/1.1 + cleared `Connection` on `/api/orders`
  (the SSE kit; POST shares the location harmlessly).
- **Default deny** — `location / { return 404; }`. Swagger UI, `/v3/api-docs`
  and `/actuator` are *not* routed (they'd need prefix rewrites and
  `X-Forwarded-Prefix`); developers reach them via `docker compose exec` or
  `kubectl port-forward`.

## Network hardening

- compose: `ports:` removed from order-service, order-service-vt,
  order-service-cs, inventory-service, shipping-service. The only published
  app entry points are `frontend:80` and `api-gateway:8080`. Mongo 27017 and
  Kafka 29092 stay published — the local `mvn spring-boot:run` flow depends
  on them.
- A named-network split (`edge`/`backend`) was considered and rejected as
  enterprise noise for a tutorial; unpublishing already makes the gateway the
  only entry from the host.
- Kubernetes was already hardened: every backend Service is ClusterIP, only
  `frontend` is NodePort. Prometheus ServiceMonitors and the CI k6/smoke jobs
  keep talking to services directly in-cluster — deliberate, so scrapes and
  load tests neither need the gateway credential path nor distort its rate
  limits.

## SSE preservation

Both proxy hops (frontend catch-all → gateway; gateway → order-service) carry
the full SSE kit: `proxy_buffering off`, `proxy_read_timeout 1h`, HTTP/1.1,
cleared `Connection` header. Both hops keep the lazy-DNS pattern
(`resolver <ip> valid=10s` + variable in `proxy_pass`) so each container
boots even when the profile-gated order-service isn't running.
