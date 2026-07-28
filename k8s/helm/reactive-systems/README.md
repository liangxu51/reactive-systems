# reactive-systems Helm chart (minikube)

Deploys the same pipeline as `docker-compose.yml` (Kafka, MongoDB,
`order-service`, `inventory-service`, `shipping-service`, `frontend`) to a
local minikube cluster. Kafka runs Zookeeper-less "KRaft" mode here (see
"Kafka KRaft migration" below) rather than `docker-compose.yml`'s
Zookeeper-based setup, so there's no separate Zookeeper component in this
chart.

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

## Backups (#36)

A `mongo-backup` CronJob runs `mongodump` against the replica set on the
schedule in `mongodb.backup.schedule` (`values.yaml`, daily at 02:00 by
default), writing a gzip archive per run to the `mongo-backup-data` PVC and
pruning archives older than `mongodb.backup.retentionDays` (default 7) on
every run. It authenticates as a dedicated `reactive-systems-backup` user
scoped to MongoDB's built-in `backup` role - not `readWrite`/root - so a
compromised backup credential can't write or delete application data.

> ⚠️ **Local PVC only, not off-cluster.** This protects against accidental
> deletes, bad writes, or replica-set corruption, but not against losing the
> node/cluster itself - archives live on the same cluster as the data
> they're backing up. Point this at real object storage (S3/GCS/etc.) before
> relying on it anywhere durability actually matters.

Trigger a backup manually instead of waiting for the schedule:

```bash
kubectl create job -n reactive-systems mongo-backup-manual --from=cronjob/mongo-backup
kubectl wait -n reactive-systems --for=condition=complete job/mongo-backup-manual --timeout=120s
```

List available archives:

```bash
kubectl run -n reactive-systems mongo-backup-shell --rm -it --restart=Never \
  --image=mongo:4.4 --overrides='{"spec":{"containers":[{"name":"mongo-backup-shell","image":"mongo:4.4","command":["ls","-la","/backup"],"volumeMounts":[{"name":"backup-data","mountPath":"/backup"}]}],"volumes":[{"name":"backup-data","persistentVolumeClaim":{"claimName":"mongo-backup-data"}}]}}'
```

### Restore runbook

MongoDB's built-in `backup` role (what the CronJob's credential holds) is
read-only by design and can't restore - `mongorestore` needs privileges
(create collections/indexes, bypass document validation) that only the
`restore` role or a full admin has. Rather than granting the automated
backup credential restore power too, this rare, human-triggered action uses
the `root` credential already documented above under Auth (#33):

```bash
ROOT_PASSWORD=$(kubectl get secret mongo-credentials -n reactive-systems -o jsonpath='{.data.root-password}' | base64 -d)
```

Restoring `--drop`s existing collections first, so this is destructive to
whatever's currently in `reactive-systems` - confirm you actually want to
overwrite it before running this against anything but a scratch/disaster-recovery
target:

```bash
kubectl run -n reactive-systems mongo-restore --rm -it --restart=Never \
  --image=mongo:4.4 --overrides='{"spec":{"containers":[{"name":"mongo-restore","image":"mongo:4.4","command":["sh","-c","mongorestore --uri=\"mongodb://root:'"$ROOT_PASSWORD"'@mongo-db:27017/reactive-systems?replicaSet=rs0\\u0026authSource=admin\" --gzip --archive=/backup/ARCHIVE_FILENAME.archive.gz --drop"],"volumeMounts":[{"name":"backup-data","mountPath":"/backup"}]}],"volumes":[{"name":"backup-data","persistentVolumeClaim":{"claimName":"mongo-backup-data"}}]}}'
```

Replace `ARCHIVE_FILENAME.archive.gz` with the archive from the "list
available archives" command above.

## Kafka HA (#40)

`kafka-broker` runs as a 3-replica StatefulSet (not a single Deployment
replica), mirroring `mongo-db`'s fix: each pod derives its `broker.id` and
`advertised.listeners` from its own stable per-pod name (`kafka-broker-0`,
`-1`, `-2`) at container start, rather than one hardcoded identity in
`values.yaml` (see `kafka.yaml`). Losing any one broker pod no longer stalls
every producer/consumer across all three services. Each broker also gets
its own PVC (`kafka.persistence` in `values.yaml`, like `mongodb.persistence`)
so a pod eviction or node drain doesn't wipe its committed log segments -
without that, losing more than one broker around the same time could still
lose data despite the replication settings below.

Two Services back the StatefulSet, not one: `kafka-broker` stays a normal
ClusterIP Service, unchanged from the single-broker version, and is what
`spring.kafka.bootstrap-servers=kafka-broker:9092` talks to. A second,
brand-new `kafka-broker-headless` Service (`clusterIP: None`) exists solely
to give the StatefulSet's pods their per-pod DNS names for
`advertised.listeners`. They're kept separate rather than turning
`kafka-broker` itself headless because `spec.clusterIP` is immutable -
flipping an already-assigned ClusterIP to `None` on `helm upgrade` would
fail the upgrade for any release installed before #40.

`kafka.defaultReplicationFactor` (3) and `kafka.minInsyncReplicas` (2) in
`values.yaml` are broker-level defaults applied to auto-created topics
(`auto.create.topics.enable` is on by default) - this covers the `orders`
topic, since nothing in this repo creates it explicitly. All three services'
Kafka producers also set `spring.kafka.producer.acks=all`, so a produce
blocks until 2 of 3 replicas have the write, not just the leader.

> ⚠️ **Only applies to a fresh topic.** `default.replication.factor` and
> `min.insync.replicas` only take effect when a topic is auto-created -
> upgrading an existing release whose `orders` topic was already created
> with replication factor 1 won't retroactively fix it. Check and fix it up
> manually instead:
>
> ```bash
> kubectl exec -n reactive-systems kafka-broker-0 -- kafka-topics \
>   --bootstrap-server localhost:9092 --describe --topic orders
> ```
>
> If `ReplicationFactor` is still `1`, reassign each partition across all
> three brokers (adjust the partition list to match `--describe`'s output)
> and then raise the topic's `min.insync.replicas`:
>
> ```bash
> kubectl exec -n reactive-systems kafka-broker-0 -- bash -c '
>   cat <<EOF > /tmp/reassign.json
> {"version":1,"partitions":[{"topic":"orders","partition":0,"replicas":[0,1,2]}]}
> EOF
>   kafka-reassign-partitions --bootstrap-server localhost:9092 \
>     --reassignment-json-file /tmp/reassign.json --execute'
>
> kubectl exec -n reactive-systems kafka-broker-0 -- kafka-configs \
>   --bootstrap-server localhost:9092 --entity-type topics --entity-name orders \
>   --alter --add-config min.insync.replicas=2
> ```

## Kafka KRaft migration (#41)

There is no `zookeeper` Deployment/Service in this chart anymore. `kafka-broker`
runs Zookeeper-less "KRaft" mode instead: each broker also acts as a
controller (`KAFKA_PROCESS_ROLES=broker,controller` - a real production
deployment would usually split those into a separate, smaller controller
quorum, but that's unwarranted complexity at 3 nodes) and the cluster
replicates its own metadata log via Raft rather than depending on an
external Zookeeper ensemble - one less stateful dependency, and one less
thing needing its own HA/backup story. `docker-compose.yml` is untouched
and still runs Zookeeper-mode Kafka, matching how #40's broker HA fix
also only touched this Helm chart.

A `kafka-cluster-id` ConfigMap (`kafka-cluster-id.yaml`) holds the
cluster's KRaft ID, generated once and persisted across `helm upgrade`
via `lookup` - the same pattern as `mongo-credentials` in
`mongo-secret.yaml`. This has to stay stable forever: once a broker has
formatted its log directory with a given cluster ID, starting it with a
different one makes it refuse to join, since it looks like an entirely
different cluster.

> ⚠️ **Not an in-place upgrade for an existing Zookeeper-mode release.**
> Zookeeper-mode brokers keep no metadata log on local disk at all (it all
> lives in Zookeeper); KRaft-mode brokers require one, formatted fresh via
> `kafka-storage.sh format`. The two are fundamentally incompatible on-disk
> - there's no config flag that converts one into the other in place, and
> this chart doesn't implement Kafka's own early-access ZooKeeper-to-KRaft
> online migration tooling (a much larger undertaking, and overkill for a
> demo project). Upgrading an existing release onto this version requires
> wiping the `kafka-broker` PVCs first, which drops whatever's currently
> in the `orders` topic (any in-flight order events) and `__consumer_offsets`
> (every service's consumer group progress):
>
> ```bash
> helm upgrade reactive-systems k8s/helm/reactive-systems -n reactive-systems  # rolls out the new StatefulSet/ConfigMap first
> kubectl scale statefulset kafka-broker -n reactive-systems --replicas=0
> kubectl delete pvc -n reactive-systems -l app=kafka-broker
> kubectl scale statefulset kafka-broker -n reactive-systems --replicas=3
> ```
>
> Do this during a lull in traffic - there's no way to preserve in-flight
> messages across the cutover.

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
