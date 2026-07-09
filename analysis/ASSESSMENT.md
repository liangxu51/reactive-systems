# Modernization Assessment — reactive-systems

**Generated:** 2026-07-08 · **Mode:** Single-system · **System dir:** repository root (`/home/liangxu/space/reactive-systems`) — no separate `legacy/` directory exists; this is the entire repo.
**Inputs:** live static analysis of the working tree at commit `b69eccd` (no prior `ASSESSMENT.md` existed, so this is a first-generation assessment, not a regeneration).
**Tooling note:** `scc`, `cloc`, and `lizard` are not installed in this environment. LOC was computed via `find` + `wc -l` grouped by extension; complexity ranking via decision-keyword counting (`if|for|while|case|catch|&&|\|\|`) per Java file. Figures are reproducible with the commands in [System Inventory](#system-inventory).

---

## Executive Summary

`reactive-systems` is a small (~2.6 KLOC application code), intentionally simple Baeldung tutorial project demonstrating a reactive, event-driven order-processing saga across three Spring WebFlux microservices (`order-service`, `inventory-service`, `shipping-service`) plus a fourth comparison variant (`order-service-vt`, blocking Spring MVC + virtual threads) — coordinated entirely through one Kafka topic and a shared MongoDB instance, with a React/Vite frontend. Overall risk is low in the "will this scale" sense (the system is deliberately tiny) but **real in the "is this safe to reuse as a starting point" sense**: every REST endpoint is unauthenticated with wildcard CORS, customer PII is logged in full, two of the four saga failure paths (`INVENTORY_FAILURE`, `INVENTORY_REVERT_FAILURE`) have no consumer at all, and two of the three core services have zero automated tests. **Headline recommendation: this system does not need a stack migration — it needs an in-place hardening and test-coverage pass** (see [Recommended Modernization Pattern](#recommended-modernization-pattern)); none of the cross-stack transformation commands are the right next step.

---

## System Inventory

**Reproduce with:**
```bash
find . -type f -name "*.<ext>" -not -path "*/node_modules/*" -not -path "*/target/*" -not -path "*/.git/*" -not -path "*/dist/*" -not -path "*/build/*" | xargs wc -l
```

| Extension | Files | LOC | Notes |
|---|---:|---:|---|
| `.java` | 52 | 1,740 | 4 Spring Boot modules (see per-service table below) |
| `.jsx` | 4 | 239 | React components |
| `.js` | 3 | 72 | React hooks/API client + Vite config |
| `.json` | 21 | 6,374 | Dominated by non-app artifacts: `package-lock.json` (999), and `.github/modernize/**` scan reports (5,262 combined — output of a separate, unrelated AppCAT/Java-upgrade tool run, not application data) |
| `.xml` | 5 | 355 | Maven POMs |
| `.yaml`/`.yml` | 12 | 647 | Spring config, Helm chart, docker-compose, k8s |
| `.html`/`.css` | 2 | 14 | Frontend shell |

**Per-service Java breakdown:**

| Service | Port | Files | LOC | Test files | Reactive/blocking |
|---|---:|---:|---:|---:|---|
| `order-service` | 8080 | 15 | 560 | 2 (unit + Testcontainers integration) | Reactive (WebFlux/Reactor) |
| `order-service-vt` | 8083 | 15 | 556 | 3 | Blocking (Spring MVC) + virtual threads |
| `inventory-service` | 8081 | 12 | 378 | **0** | Reactive |
| `shipping-service` | 8082 (no HTTP surface) | 10 | 246 | **0** | Reactive |

**Technology fingerprint** (confirmed via `pom.xml`/`package.json`, not inferred):
- Java 21, Maven multi-module, **Spring Boot 4.0.7** parent (root `pom.xml:14`) — note this contradicts CLAUDE.md/README's "Spring Boot 3" framing; see [Documentation Gaps](#documentation-gaps).
- `order-service`, `inventory-service`, `shipping-service`: `spring-boot-starter-webflux` + `spring-boot-starter-data-mongodb-reactive` + `spring-boot-starter-kafka`, Lombok, `springdoc-openapi-starter-webflux-ui:3.0.3` (order-service only).
- `order-service-vt`: `spring-boot-starter-web` (blocking) + `spring-boot-starter-data-mongodb` (blocking) + `spring.threads.virtual.enabled=true` — a deliberate reactive-vs-virtual-threads comparison variant, not a duplicate or dead module (see Architecture section).
- Frontend: React 19.2.7 + Vite 8.1.3, plain JavaScript (no TypeScript), Bootstrap 4.5.0.
- Infra: Kafka + Zookeeper, MongoDB (single-node replica set `rs0` in Helm, plain standalone in docker-compose), Docker Compose + Helm chart (minikube-targeted).
- No CI workflows present (`.github/` contains only prior automated-tool scan artifacts, no `.github/workflows/`).

---

## Architecture-at-a-Glance

Nine functional domains identified: **Order Orchestration (reactive)**, **Order Orchestration (virtual-thread variant)**, **Inventory/Stock Reservation**, **Shipping/Fulfillment**, **Kafka Event Contract** (shared `OrderStatus` + duplicated domain model + dual Jackson 2/3 serializers), **Persistence (MongoDB)**, **REST API/Streaming layer**, **Frontend (React/Vite UI)**, and **Container/Orchestration Infra**. Full domain table with file citations and the domain dependency diagram: `analysis/ARCHITECTURE.mmd`.

`order-service-vt` is confirmed (via its own `pom.xml` description, Helm chart `fail` guard preventing simultaneous deployment with `order-service`, and line-for-line ported tests) to be a **deliberate architecture-comparison companion**, not orphaned scaffolding — it reimplements the identical saga against the same Kafka topic and Mongo collection using blocking MVC + virtual threads instead of WebFlux/Reactor.

**Dangling references found:**
- `inventory-service`'s Mongo seed loader (`AsyncApplication.java:23-31`) is self-documented as broken (`// TODO: This does not work for reactive repositories from Mongo`) — `data.json` never loads under any deployment, confirmed by the Helm README's separate manual `mongo --eval` seeding workaround.
- `shipping-service/domain/Order.java` is missing the Jackson-3 `ObjectIdValueSerializer` annotation that every other copy of `Order` carries — currently harmless (shipping-service has no REST controller serializing `Order`) but a trap for whoever adds one later.
- A documented-but-unreproduced runtime bug: `k8s/helm/reactive-systems/README.md:82-89` reports `shipping-service` failing to publish `SHIPPING_SUCCESS`/`SHIPPING_FAILURE` with a `LocalDate` Jackson `InvalidDefinitionException`, consistent with the confirmed absence of `jackson-datatype-jsr310` in any `pom.xml`. Flagged as **Confidence: Medium** — needs SME reproduction.

---

## Production Runtime Profile

No telemetry available — this is a local/tutorial project with no APM, no production deployment, and no CI. Step skipped per the assessment procedure; no runtime wall-clock or variance data exists to overlay on the domain map.

---

## Technical Debt

Ranked by remediation value (full citations from the legacy-analyst subagent):

1. **`inventory-service` and `shipping-service` have zero automated tests**, yet own the two riskiest business rules in the saga: stock check-and-decrement (`ProductService.java:26-45`) and the hardcoded 10:00–18:00 shipping-acceptance window (`ShippingService.java:29-30`).
2. **`.subscribe()` with no `onError` consumer** in the inventory and shipping Kafka consumers (`inventory-service/.../async/consumer/OrderConsumer.java:30-54`, `shipping-service/.../async/consumer/OrderConsumer.java:30-42`) — `doOnError` only peeks, it doesn't handle; an unhandled terminal error can destabilize the listener and prevent the very failure-notification the saga depends on.
3. **No consumer anywhere handles `INVENTORY_FAILURE` or `INVENTORY_REVERT_FAILURE`** — `order-service`'s `NEXT_STATUS` map (`async/consumer/OrderConsumer.java:20-23`) only covers the happy-path + `SHIPPING_FAILURE`. A failed stock revert is permanently silent with no retry or alert.
4. **Frontend API base URLs are hardcoded to `localhost`** (`frontend/src/api/ordersApi.js:1-2`, `useOrderStream.js:3`), which silently breaks the Helm/k8s deployment this same repo ships — the Helm README works around it by instructing `kubectl port-forward svc/order-service 8080:8080` rather than fixing the frontend.
5. **Non-atomic read-check-decrement on stock** in `ProductService.handleOrder` (`inventory-service/.../ProductService.java:26-45`) relies on `@Transactional` + `ReactiveMongoTransactionManager` to catch write conflicts at commit time, but nothing retries on conflict — concurrent orders for the same product surface as generic consumer errors instead of controlled oversell prevention.
6. **Dead seed-data loader** — see Dangling References above; self-documented broken code that looks functional to a new contributor.
7. **No Kafka error-handling/dead-letter strategy** anywhere (`ErrorHandler|RetryTemplate|DeadLetter` — zero matches repo-wide). A poison-pill message on the single shared `orders` topic has no DLT route.
8. **Wildcard CORS with no authentication** on every controller (`@CrossOrigin(origins = "*")`, no Spring Security dependency anywhere) — also see Security Findings below.
9. **Helm chart pins every image to mutable `:latest`** with `imagePullPolicy: IfNotPresent` (`values.yaml:9,26-63`) — a rebuilt image with the same tag won't be re-pulled, producing non-reproducible deployments.
10. **`order-service-vt` can publish `INITIATION_SUCCESS` and then locally downgrade the same order to `FAILURE`** on a later save exception (`vt/service/OrderService.java:26-41`) — the already-published success event has no retraction, a smaller-scope version of the dual-write problem the saga otherwise handles correctly.

Not counted as debt (intentional, documented design choices): the `Order`/`LineItem`/`Address`/`OrderStatus`/`ObjectIdSerializer` duplication across services (explicit in CLAUDE.md, for independent deployability), and the dual Jackson 2/3 serializer annotations (a managed consequence of Spring Boot 4's Jackson 3 default alongside Spring Kafka's continued Jackson 2 dependency).

---

## Security Findings

| CWE | Severity | Location | Description |
|---|---|---|---|
| CWE-306 / CWE-862 | High | `order-service/.../OrderController.java:28-45`, `inventory-service/.../ProductController.java:24-28`, `order-service-vt/.../OrderController.java` | No authentication/authorization on any REST endpoint. `GET /api/orders` returns every order in the DB — including every customer's name and address — to any anonymous caller. |
| CWE-284 (IDOR-style) | High | `order-service/.../OrderService.java:48-51` | `getOrders()` calls `findAll()` with no owner/tenant scoping at all — worse than a guessable-ID issue, the entire order table is one unauthenticated collection endpoint. |
| CWE-942 | Medium | `OrderController.java:21`, `ProductController.java:17`, VT `OrderController.java` | `@CrossOrigin(origins = "*")` on every controller; combined with the above, any malicious website can exfiltrate the full order/customer list from a victim's browser. |
| CWE-532 | Medium | `order-service`, `inventory-service`, `shipping-service` — every `OrderConsumer`/`OrderService`/`ProductService` log call | Full `Order` object (Lombok `toString()`) logged at INFO across all three services, including unredacted `shippingAddress` (name/house/street/city/zip) and `userId`. |
| CWE-20 | Medium | `order-service/.../domain/{Order,Address,LineItem}.java`, `OrderController.java:29` | No Bean Validation anywhere on request-bound domain classes; `Order.total` is never recomputed server-side from `Product.price × quantity`, so a client can submit an arbitrary total. |
| CWE-306 / CWE-284 | High | `docker-compose.yml:14-35`, `k8s/helm/.../mongodb.yaml`, `kafka.yaml` | MongoDB and Kafka run with zero authentication, published on reachable ports. Anyone with network access gets unauthenticated full read/write to the DB and can forge saga messages (e.g. a fabricated `SHIPPING_SUCCESS` for an order never actually shipped). |
| CWE-250 | Low | All four service `Dockerfile`s | `java -jar` runs as the container's default root user — no `USER` directive. |
| CWE-200 | Low | `order-service/pom.xml` (springdoc) | Swagger UI / OpenAPI JSON exposed with no auth restriction — low incremental risk today only because the API is already unauthenticated. |

No hardcoded credentials were found anywhere in the repo (grepped for password/secret/API-key/token/AKIA/private-key patterns across all source, config, and script files — zero matches). No `SECRETS.local.md` was generated as a result.

**Positive controls confirmed:** Kafka JSON deserialization is correctly hardened (`spring.json.add.type.headers=false` + a fixed default type defeats the classic Jackson/Spring-Kafka arbitrary-type deserialization attack); no OS command execution surface; no raw/string-concatenated Mongo queries (NoSQL injection not reachable); React's default text escaping prevents XSS from order/status fields; no stack-trace leakage to clients; no Actuator dependency (no exposed `/actuator/env` etc.).

**Context:** given CLAUDE.md's own framing ("tutorial... to illustrate reactive systems concepts") and the Helm chart's "targets a local minikube cluster" comment, the total absence of auth is very likely a deliberate teaching simplification, not an oversight — but neither the root README nor the Helm README currently states "not for production use" anywhere, which they should if this pattern might get copy-pasted into a real service.

---

## Documentation Gaps

Top 5, ranked by how likely they are to mislead a new contributor:

1. **`k8s/helm/reactive-systems/README.md` describes building the frontend as an Angular 9 project** ("this Angular 9 project uses a webpack version that needs OpenSSL's legacy provider") — stale since commit `15f9315` rewrote the frontend to React + Vite. A reader following this doc today would hit a build workaround for a framework that no longer exists in the repo.
2. **No "not for production" callout anywhere** for the unauthenticated Mongo/Kafka/REST setup (see Security Findings) — the Helm README documents deployment steps in detail but never states the trust-boundary assumption they rely on.
3. **The saga's failure/compensation paths are undocumented outside the code itself** — CLAUDE.md's architecture diagram shows the happy path plus one compensating transaction (`SHIPPING_FAILURE` → `REVERT_INVENTORY`), but doesn't mention that `INVENTORY_FAILURE`/`INVENTORY_REVERT_FAILURE` have no consumer at all (Technical Debt #3) — a reader would reasonably assume the saga is fully compensating when two of its four failure states are silently dropped.
4. **`order-service-vt` has no README of its own** explaining its relationship to `order-service` — a new contributor finds two "order service" directories with only a one-line `pom.xml` `<description>` and the Helm README's deployment notes to explain that they're a deliberate, mutually-exclusive comparison pair rather than a migration-in-progress or duplicate.
5. **`inventory-service`/`shipping-service` have no service-level README** documenting the business rules they own (stock reservation semantics, the hardcoded shipping-acceptance window) — only the generic Spring Initializr `HELP.md` boilerplate, so those rules exist solely as source code with no narrative explanation.

---

## Relative Scale

**COCOMO-II basic index** (application code only — Java + JSX + JS, excluding config/lockfiles/vendored reports): `2.94 × (2.051 KLOC)^1.10 ≈ 6.48`.

This is a **relative complexity/scale signal only**, useful for ranking this system against others in a portfolio — it is **not a timeline or cost estimate**. The underlying COCOMO formula assumes traditional human-team productivity curves, which agentic modernization does not follow; no person-months, schedule, or dollar figure is implied by this number. For context, an index this low places `reactive-systems` at the very small end of any realistic portfolio — consistent with its stated purpose as a teaching artifact rather than a production system.

---

## Recommended Modernization Pattern

**Refactor-in-place — hardening and test-coverage paydown, not a stack migration.**

None of the findings in this assessment call for a version bump, a cross-stack rewrite, or a greenfield rebuild: the stack (Java 21, Spring Boot 4.0.7, WebFlux/Reactor, React 19/Vite) is already current, and the architecture (Kafka saga + per-service Mongo collections) is sound for the system's stated teaching purpose. The actual work is:

- **Security hardening** of the findings above (auth, CORS, PII logging, input validation, infra auth) → route to `/modernize-harden`, which is built for exactly this kind of OWASP/CWE remediation pass with a reviewable patch.
- **Test coverage and saga-completeness fixes** (Technical Debt #1–3, #5, #10) — these are hand-written engineering fixes and new test suites, not a pattern any of the transformation commands (`/modernize-uplift`, `/modernize-transform`, `/modernize-reimagine`) are designed to produce, since none of them involve a stack or version change. Recommend addressing directly as normal engineering work, informed by this assessment's file:line citations.
- **Documentation sync** (stale Angular reference, missing saga-failure-path docs, missing service READMEs) — a short, low-risk doc pass.

If a genuine target-stack decision is made later (e.g., consolidating `order-service`/`order-service-vt` into one implementation, or moving off Kafka), re-run this assessment's routing logic then — today there's no such decision pending in the codebase or its docs.

---
