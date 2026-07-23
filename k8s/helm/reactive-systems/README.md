# reactive-systems Helm chart (minikube)

Deploys the same pipeline as `docker-compose.yml` (Zookeeper, Kafka, MongoDB,
`order-service`, `inventory-service`, `shipping-service`, `frontend`) to a
local minikube cluster.

> ⚠️ **Demo only — not for production.** These services have no authentication or authorization; `GET /api/orders` exposes all order data (including customer PII) to any caller. Add auth and scope data access before any real deployment.

## 1. Point Docker at minikube and build the images

No registry is used — images are built straight into minikube's own Docker
daemon and referenced with `imagePullPolicy: IfNotPresent`. Because of that,
tag every rebuild with this chart's `appVersion` (bump it in `Chart.yaml`
first) rather than `latest` — with `IfNotPresent`, a node that already
pulled `order-service:latest` once will keep serving that stale image
forever, silently skipping any later rebuild under the same tag.

```bash
minikube start
eval $(minikube docker-env)

mvn clean package -pl order-service,inventory-service,shipping-service

# Bump appVersion in k8s/helm/reactive-systems/Chart.yaml first, then:
TAG=$(grep '^appVersion' k8s/helm/reactive-systems/Chart.yaml | cut -d '"' -f2)
docker build -t order-service:$TAG order-service
docker build -t inventory-service:$TAG inventory-service
docker build -t shipping-service:$TAG shipping-service

cd frontend && npm ci && npm run build && cd ..
docker build -t frontend:$TAG frontend
```

(On Node 17+, `npm run build` fails with `error:0308010C:digital envelope
routines::unsupported` — this Angular 9 project uses a webpack version that
needs OpenSSL's legacy provider: prefix the build with
`NODE_OPTIONS=--openssl-legacy-provider`.)

(To use the virtual-thread `order-service-vt` module instead, also run
`mvn clean package -pl order-service-vt` and
`docker build -t order-service-vt:$TAG order-service-vt`.)

Each service's `image.tag` in `values.yaml` defaults to empty, which falls
back to `Chart.yaml`'s `appVersion` at install/upgrade time. To rebuild just
one service without bumping the chart version, override its tag directly,
e.g. `--set orderService.image.tag=$(git rev-parse --short HEAD)`.

## 2. Install the chart

Installed into its own namespace so it doesn't mix with anything else
already running on the cluster:

```bash
helm install reactive-systems k8s/helm/reactive-systems \
  --namespace reactive-systems --create-namespace
```

To run `order-service-vt` instead of `order-service`:

```bash
helm install reactive-systems k8s/helm/reactive-systems \
  --namespace reactive-systems --create-namespace \
  --set orderService.enabled=false \
  --set orderServiceVt.enabled=true
```

## 3. Reach the app

The frontend calls `order-service`/`inventory-service` through relative
`/api/...` paths, which nginx proxies in-cluster to the `order-service` and
`inventory-service` Services - no port-forwarding needed. Just open the UI:

```bash
minikube service frontend -n reactive-systems
```

If nginx logs `502` on `/api/orders` or `/api/products`, your cluster's
CoreDNS ClusterIP likely differs from the default this chart assumes
(`frontend.dnsResolverIP`, see `values.yaml`) - override it with:

```bash
--set frontend.dnsResolverIP=$(kubectl get svc kube-dns -n kube-system -o jsonpath='{.spec.clusterIP}')
```

## Seed product data

`inventory-service` doesn't load `src/main/resources/data.json` on its own
(no `CommandLineRunner`/Mongo import is wired up), so `product` starts out
empty regardless of deployment platform. Seed it once after Mongo comes up:

Mongo requires auth now (see #33), so pull the app user's password out of the
generated Secret first:

```bash
APP_PASSWORD=$(kubectl get secret mongo-credentials -n reactive-systems -o jsonpath='{.data.app-password}' | base64 -d)

kubectl exec -n reactive-systems mongo-db-0 -- mongo "mongodb://reactive-systems-app:${APP_PASSWORD}@localhost:27017/reactive-systems?authSource=admin" --eval '
db.product.insertMany([
  {_id: ObjectId("5edcbfd30717397ae8cfb7f0"), name: "Product A", price: NumberLong(12), stock: 100},
  {_id: ObjectId("5edcbfd30717397ae8cfb7f1"), name: "Product D", price: NumberLong(16), stock: 100}
]);'
```

## Notes

- MongoDB and Kafka Services are named `mongo-db` and `kafka-broker` to
  match the hostnames already baked into each service's
  `application-docker.properties` (the `docker` Spring profile is what the
  Dockerfiles activate) — no code changes needed.
- `mongo-db` runs as a 3-member replica set (`rs0`), started via
  `mongod --replSet rs0` and initiated by the `mongo-init` sidecar container
  in the `mongo-db-0` pod (see `mongodb.yaml`). This is required because
  `inventory-service` uses reactive MongoDB transactions, which MongoDB only
  allows on a replica set (a plain standalone `mongod`, as in
  `docker-compose.yml`, fails these with `Transaction numbers are only
  allowed on a replica set member or mongos`).
- `mongo-db` uses a PersistentVolumeClaim (`mongodb.persistence` in
  `values.yaml`); disable it for an ephemeral `emptyDir` instead.
- **Auth** (#33): `mongod` runs with `--keyFile` (internal replica-set auth)
  and requires client auth. Credentials are generated once per release into
  the `mongo-credentials` Secret - a `root` admin user and a shared
  least-privilege `reactive-systems-app` user (`readWrite` on the
  `reactive-systems` db only), both created by the `mongo-init` sidecar via
  MongoDB's "localhost exception" the first time `mongo-db-0` comes up. The
  three app services get the app user's password injected via
  `SPRING_MONGODB_URI` (see `_helpers.tpl`/`mongo-secret.yaml`) rather than
  it living in `application-docker.properties`. Retrieve the root password
  with:
  ```bash
  kubectl get secret mongo-credentials -n reactive-systems -o jsonpath='{.data.root-password}' | base64 -d
  ```
- `order-service` and `order-service-vt` are mutually exclusive, mirroring
  the `order-service` / `order-service-vt` docker-compose profiles — both
  consume/produce against the same Kafka topic and Mongo collection.
- `shipping-service` has no `spring-boot-starter-webflux` dependency (unlike
  the other two services), so it never opens an HTTP port — it's a pure
  Kafka consumer/producer. Its Deployment intentionally has no Service,
  ports, or probes.
- **Encryption for local testing**: Optionally enable Istio on minikube for encrypted (mTLS) pod-to-pod communication. See `k8s/ISTIO_MINIKUBE.md` for setup and testing instructions.

## Fixed: shipping-service Order.shippingDate bug

Previously, once an order reached `PREPARE_SHIPPING`, `shipping-service`
could fail to publish `SHIPPING_SUCCESS`/`SHIPPING_FAILURE` back to Kafka
with an `InvalidDefinitionException` on `Order.shippingDate`. The root
cause turned out to be more than a missing Jackson module: `shipping-service`'s
duplicated `Order.shippingDate` was typed `java.time.LocalDate` while
`order-service`'s copy is `java.util.Date` — once a real value flowed
through, the type mismatch broke deserialization on the `order-service`
side and permanently wedged its Kafka consumer, not just this one order.
Fixed in PR #12 by aligning both services' wire-facing type to `Date`.
See `analysis/ASSESSMENT.md` Technical Debt #12 for the full writeup.
