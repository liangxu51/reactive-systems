# Load test: the full order saga

A [k6](https://k6.io/) script (`k6/order-saga.js`) that drives `POST /api/orders`
under ramping concurrent load and lets the resulting saga - `order-service` ->
Kafka `RESERVE_INVENTORY` -> `inventory-service` -> Kafka `PREPARE_SHIPPING` ->
`shipping-service` - play out for real against a live minikube deployment
(`k8s/helm/reactive-systems/`). The Job below targets in-cluster DNS
(`order-service:8080`, the Prometheus Service), so it only runs against the
Helm deployment - not against a service started by its local Testcontainers
launcher.

Runs as a one-off `Job` inside the cluster (`k8s/job.yaml`) rather than from a
laptop through `kubectl port-forward`, so the forwarded tunnel itself never
becomes the bottleneck under load - same reasoning as the `mongo-backup-shell`/
`mongo-restore` one-off pods in the chart's own README.

## Results in Grafana

k6 pushes its own metrics straight into the same Prometheus this chart
already deploys, via `--out experimental-prometheus-rw`, so load-test results
sit alongside the app's own request/consumer-lag/replica metrics instead of a
separate report to cross-reference by hand. This needs
`kube-prometheus-stack.prometheus.prometheusSpec.enableRemoteWriteReceiver: true`
(added to `values.yaml` alongside this directory - `helm upgrade` before the
first run if you're on an older release).

Confirmed live: metrics land under the `k6_` prefix -
`k6_http_reqs_total`, `k6_http_req_duration_p95`/`_p99`, `k6_http_req_failed_rate`,
`k6_checks_rate`, `k6_vus`, plus this script's own custom ones
(`k6_orders_created_total`, `k6_order_create_duration_p95`/`_p99` - all seen
in a live smoke run). `k6_orders_rejected_total` follows the same naming
convention but only gets pushed once at least one request actually fails -
it wasn't observed in that run because every order was accepted. Explore them in Grafana
(`kubectl port-forward -n reactive-systems svc/reactive-systems-grafana 3000:80`)
against the `Prometheus` datasource, or build a dashboard panel the same way
as the existing "reactive-systems overview" one
(`k8s/helm/reactive-systems/grafana-dashboards.yaml`).

## Before a real (not smoke-test) run: stock

`inventory-service`'s `product` collection is seeded with only `stock: 100`
per product (see the chart README's "Seed product data" step) - a sustained
run at real concurrency exhausts that in seconds, after which every further
order legitimately fails at `RESERVE_INVENTORY` (`INVENTORY_FAILURE`, no
compensating `REVERT_INVENTORY` needed since nothing was reserved). That's
correct saga behavior, not a bug - but if you want to see sustained
`SHIPPING_SUCCESS` throughput rather than mostly failures, bump stock first:

```bash
scripts/mongo-shell.sh --eval 'db.product.updateMany({}, {$set: {stock: 1000000}})'
```

(`scripts/mongo-shell.sh` is from the `reactive-systems-creds` skill/
`.claude/skills/reactive-systems-creds/` - handles auth/replicaSet flags the
bare `mongo:4.4` image needs.)

## Before a real run: the shipping window

`shipping-service` only accepts orders between 10:00-18:00 **server clock**
(`ShippingService.SHIPPING_WINDOW_START`/`_END`, `shipping-service`'s own
container timezone - confirmed live to be UTC, not the host's). Outside that
window every order will complete the saga up through `INVENTORY_SUCCESS` and
then get `SHIPPING_FAILURE`, reverting inventory - again, correct behavior,
just worth knowing before reading "everything failed" as a bug.

## Running it

```bash
# One-time per cluster (or after editing the script):
kubectl create configmap k6-order-saga-script -n reactive-systems \
  --from-file=order-saga.js=load-test/k6/order-saga.js \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl apply -f load-test/k8s/job.yaml
kubectl logs -n reactive-systems -l job-name=k6-order-saga-load-test -f
```

Default load profile: ramp to 10 VUs over 30s, hold 2m, ramp down 30s (each
VU loops ~1 order every 2-4s, so roughly 3-5 orders/sec at the 10-VU
plateau). Adjust before applying by editing the `TARGET_VUS`/`RAMP_UP`/`HOLD`/
`RAMP_DOWN` env vars in `k8s/job.yaml` - Jobs are immutable once created, so
re-`kubectl delete job/... && kubectl apply -f ...` to change them.

Re-running needs the old Job deleted first (`kubectl delete job
k6-order-saga-load-test -n reactive-systems`) since `metadata.name` collides
otherwise; `ttlSecondsAfterFinished: 3600` also auto-cleans a finished Job an
hour after completion if you forget.

## What the script does

Each VU iteration builds an order with 1-2 line items against the two seeded
product IDs, `POST`s it to `/api/orders` with HTTP Basic auth (credentials
come from the existing `api-credentials` Secret via `secretKeyRef`, never
hardcoded), and records:

- `orders_created` / `orders_rejected` - counters, split on HTTP 2xx vs not.
- `order_create_duration` - a `Trend` on this endpoint specifically (k6's
  built-in `http_req_duration` also works, but is shared across every
  request tag in the run).

Thresholds (`http_req_failed rate < 5%`, `order_create_duration p(95) <
2000ms`) fail the k6 run's exit code if breached - useful as a CI-style gate
later, though nothing wires this into `.github/workflows/ci.yml` today (that
pipeline's `deploy-smoke-test` job spins up an ephemeral `kind` cluster with
no seeded product data or realistic load profile to run this against
meaningfully).

This intentionally only measures the `POST /api/orders` call itself, not the
full saga's end-to-end latency - `order-service` returns as soon as the order
is saved with `INITIATION_SUCCESS` (see `OrderService.createOrder`), before
Kafka/inventory/shipping ever run. There's no `GET /api/orders/{id}` to poll
per-order, and polling `GET /api/orders` (returns every order, unbounded) at
load-test frequency would become its own bottleneck. Instead, confirm the
saga itself kept up after a run via the order-status breakdown directly in
Mongo:

```bash
scripts/mongo-shell.sh --eval "
db.order.aggregate([{\$group: {_id: '\$orderStatus', count: {\$sum: 1}}}])
  .forEach(printjson)"
```

and Kafka consumer lag / Mongo replica health, both already scraped and
alerted on by this chart's existing `KafkaConsumerGroupLagHigh`/
`MongoReplicaSetNoPrimary` `PrometheusRule`s (see the Helm chart README's
"Centralized monitoring and alerting" section) - a saga that can't keep up
with `order-service`'s accept rate shows up there as growing lag, not as a
slow `POST /api/orders`.

## Cleanup

```bash
kubectl delete job k6-order-saga-load-test -n reactive-systems
kubectl delete configmap k6-order-saga-script -n reactive-systems
```

Doesn't delete the orders/products it created in Mongo - they're ordinary
`order`/`product` documents, cleaned up the same way as any other test data
in this demo (there's no dedicated teardown script).
