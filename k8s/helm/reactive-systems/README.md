# reactive-systems Helm chart (minikube)

Deploys the same pipeline as `docker-compose.yml` (Zookeeper, Kafka, MongoDB,
`order-service`, `inventory-service`, `shipping-service`, `frontend`) to a
local minikube cluster.

## 1. Point Docker at minikube and build the images

No registry is used — images are built straight into minikube's own Docker
daemon and referenced with `imagePullPolicy: IfNotPresent`.

```bash
minikube start
eval $(minikube docker-env)

mvn clean package -pl order-service,inventory-service,shipping-service

docker build -t order-service:latest order-service
docker build -t inventory-service:latest inventory-service
docker build -t shipping-service:latest shipping-service

cd frontend && npm ci && npm run build && cd ..
docker build -t frontend:latest frontend
```

(On Node 17+, `npm run build` fails with `error:0308010C:digital envelope
routines::unsupported` — this Angular 9 project uses a webpack version that
needs OpenSSL's legacy provider: prefix the build with
`NODE_OPTIONS=--openssl-legacy-provider`.)

(To use the virtual-thread `order-service-vt` module instead, also run
`mvn clean package -pl order-service-vt` and
`docker build -t order-service-vt:latest order-service-vt`.)

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

The frontend is hardcoded to call `http://localhost:8080/api/orders`, so
`order-service` needs to land on `localhost:8080` same as with
docker-compose:

```bash
kubectl port-forward svc/order-service 8080:8080 -n reactive-systems
```

Then open the UI:

```bash
minikube service frontend -n reactive-systems
```

## Seed product data

`inventory-service` doesn't load `src/main/resources/data.json` on its own
(no `CommandLineRunner`/Mongo import is wired up), so `product` starts out
empty regardless of deployment platform. Seed it once after Mongo comes up:

```bash
kubectl exec -n reactive-systems deploy/mongo-db -- mongo reactive-systems --eval '
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
- `mongo-db` runs as a single-node replica set (`rs0`), started via
  `mongod --replSet rs0` and initiated by the `mongo-init-replicaset` Helm
  hook Job. This is required because `inventory-service` uses reactive
  MongoDB transactions, which MongoDB only allows on a replica set (a plain
  standalone `mongod`, as in `docker-compose.yml`, fails these with
  `Transaction numbers are only allowed on a replica set member or mongos`).
- `mongo-db` uses a PersistentVolumeClaim (`mongodb.persistence` in
  `values.yaml`); disable it for an ephemeral `emptyDir` instead.
- `order-service` and `order-service-vt` are mutually exclusive, mirroring
  the `order-service` / `order-service-vt` docker-compose profiles — both
  consume/produce against the same Kafka topic and Mongo collection.
- `shipping-service` has no `spring-boot-starter-webflux` dependency (unlike
  the other two services), so it never opens an HTTP port — it's a pure
  Kafka consumer/producer. Its Deployment intentionally has no Service,
  ports, or probes.

## Known pre-existing bug hit during testing

Once an order reaches `PREPARE_SHIPPING`, `shipping-service` fails to
publish `SHIPPING_SUCCESS`/`SHIPPING_FAILURE` back to Kafka:
`InvalidDefinitionException: Java 8 date/time type 'java.time.LocalDate' not
supported by default` when serializing `Order.shippingDate`. This is an
application bug (the Kafka producer's Jackson `ObjectMapper` is missing the
`jackson-datatype-jsr310` module) present in the `shipping-service` module
itself, not something introduced by this chart — it would reproduce the
same way under `docker-compose`.
