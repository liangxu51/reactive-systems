GKE Deployment guide — Dataplane V2 (Pod-to-Pod Encryption)

Overview

This project runs all services (order-service, inventory-service, shipping-service, frontend) and MongoDB in a Kubernetes cluster. For in-cluster deployments, enable GKE Dataplane V2 to provide transparent pod-to-pod in-transit encryption (eBPF-based) without changing application code.

Why Dataplane V2

- Transparent kernel-level encryption for all pod-to-pod traffic
- No changes required to application connection strings or driver configs
- Protects service → MongoDB (in-cluster) and inter-service traffic, and MongoDB replica-set internals
- Lower operational complexity than managing application TLS certs for every service

When to use Istio instead

- Istio provides mTLS plus rich traffic policy, observability, routing (canary, retries), and RBAC
- Choose Istio when you need fine-grained traffic policies, telemetry, or multi-cluster mesh features
- Dataplane V2 and Istio overlap on encryption; combine only if you need Istio features beyond encryption

Enable Dataplane V2

Recommended: create a new cluster with Dataplane V2 enabled.

Example (gcloud):

```bash
gcloud container clusters create reactive-systems-cluster \
  --zone=us-central1-c \
  --num-nodes=3 \
  --machine-type=e2-standard-4 \
  --enable-dataplane-v2 \
  --enable-network-policy
```

To enable on an existing cluster (note: may depend on cluster version and upgrade path):

```bash
gcloud container clusters update reactive-systems-cluster \
  --enable-dataplane-v2
```

Verify Dataplane V2 is enabled

```bash
gcloud container clusters describe reactive-systems-cluster --zone=us-central1-c \
  --format="value(dataplaneV2Config.enabled)"
# should print: true
```

Operational notes

- Dataplane V2 is pod-to-pod only. It does not encrypt traffic leaving the cluster (ingress) — keep API Gateway/Load Balancer TLS for external clients.
- Dataplane V2 protects in-cluster MongoDB traffic; if MongoDB is external (Atlas, managed), you still need app-level TLS.
- Expected performance impact: small (~5–10%). Benchmark under load before rollout.
- Dataplane V2 does not provide traffic policies or telemetry — use a service mesh (Istio/Anthos) when those features are needed.

Kubernetes manifests and Helm

- No application-level TLS config is required for in-cluster MongoDB when Dataplane V2 is enabled. Remove or ignore app-level TLS flags intended for host-based deployments.
- Ensure Services and DNS names remain consistent with the chart (the Helm chart uses service names like `mongo-db` and `kafka-broker`).

Migration checklist

1. Create cluster with Dataplane V2 enabled
2. Deploy Helm chart (`k8s/helm/reactive-systems`) into `reactive-systems` namespace
3. Verify pods can communicate and that `mongo-db` replica set functions
4. Run integration smoke tests (order creation, inventory reservation, shipping flow)
5. Monitor CPU/latency and tune node sizing if necessary

References

- GKE Dataplane V2: https://cloud.google.com/kubernetes-engine/docs/how-to/dataplane-v2
- Anthos Service Mesh / Istio: https://cloud.google.com/service-mesh
- GKE network policy: https://cloud.google.com/kubernetes-engine/docs/how-to/network-policy
