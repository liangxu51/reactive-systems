# Istio Service Mesh on Minikube

Enable mTLS and traffic policies for local minikube development using Istio.

## Why Istio on Minikube

- Encrypted pod-to-pod communication (mTLS) with automatic certificate management
- Traffic policies, observability, and fine-grained routing
- Local testing of service-mesh patterns before production GKE deployment
- Complements Dataplane V2 (GKE) — Istio provides both encryption and policies

## Prerequisites

- minikube v1.20+ (supports Istio)
- 4+ CPUs, 4+ GB memory (Istio adds ~500MB overhead)
- kubectl
- Helm 3+

## 1. Start Minikube with Extra Resources

```bash
minikube start \
  --cpus=4 \
  --memory=6144 \
  --driver=docker \
  --insecure-registry=localhost:5000
```

## 2. Install Istio

Download and install Istio (v1.18+ recommended):

```bash
# Download Istio
curl -L https://istio.io/downloadIstio | sh -
cd istio-1.18.0  # or newer version
export PATH=$PWD/bin:$PATH

# Install Istio on minikube (demo profile)
istioctl install --set profile=demo -y

# Create istio-system namespace and enable sidecar injection
kubectl create namespace istio-system 2>/dev/null || true
kubectl label namespace default istio-injection=enabled --overwrite
```

Verify installation:

```bash
kubectl get pods -n istio-system
# Should show istiod, ingress gateway, etc.
```

## 3. Deploy the Helm Chart

Deploy the reactive-systems chart with sidecar injection enabled:

```bash
helm install reactive-systems k8s/helm/reactive-systems \
  --namespace reactive-systems --create-namespace \
  --set global.istio.enabled=true
```

If your values.yaml doesn't have an `istio` section, just deploy normally:

```bash
helm install reactive-systems k8s/helm/reactive-systems \
  --namespace reactive-systems --create-namespace
```

Verify sidecars are injected (each pod should have 2/2 containers):

```bash
kubectl get pods -n reactive-systems
# order-service-xxx           2/2     Running   0       ...
# inventory-service-xxx       2/2     Running   0       ...
# shipping-service-xxx        2/2     Running   0       ...
# mongo-db-0                  2/2     Running   0       ...
# kafka-broker-xxx            2/2     Running   0       ...
# frontend-xxx                2/2     Running   0       ...
```

## 4. Verify mTLS

Check that mTLS is active:

```bash
# Enter a pod and curl another service
kubectl exec -it deploy/order-service -n reactive-systems -c order-service -- sh

# Inside the pod, try to curl inventory-service
curl https://inventory-service.reactive-systems.svc.cluster.local:8081/api/products \
  --cacert /etc/ssl/certs/ca-certificates.crt

# Should succeed with 200 OK (Envoy proxy handles mTLS)
```

Alternatively, check mTLS status with Kiali (if installed):

```bash
kubectl get virtualservices,destinationrules -n reactive-systems
```

## 5. Access the Frontend

Port-forward to the frontend:

```bash
kubectl port-forward svc/frontend -n reactive-systems 8080:80
```

Open http://localhost:8080 in your browser.

## 6. Enable Observability (Optional)

Install Kiali for visualizing the service mesh:

```bash
kubectl apply -f https://raw.githubusercontent.com/istio/istio/release-1.18/samples/addons/kiali.yaml

# Port-forward Kiali
kubectl port-forward svc/kiali -n istio-system 20000:20000
```

Open http://localhost:20000 (username: admin, password: admin by default).

## 7. Test the Flow

Run a test order to verify encrypted pod-to-pod communication:

```bash
kubectl port-forward svc/order-service -n reactive-systems 8080:8080

# In another terminal, post an order
curl -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customer": "Test",
    "lineItems": [{"productId": "5edcbfd30717397ae8cfb7f0", "quantity": 1}],
    "shippingAddress": {"street": "123 Main", "city": "Anytown", "country": "USA"}
  }'
```

Monitor in Kiali to see encrypted traffic flowing between services.

## Cleanup

When done with Istio:

```bash
# Remove sidecar injection label
kubectl label namespace default istio-injection-

# Uninstall Istio
istioctl uninstall --purge -y

# Remove the istio-system namespace
kubectl delete namespace istio-system
```

## Performance Notes

- mTLS adds ~5-10ms latency per request (acceptable for local dev)
- Istio adds ~500MB memory overhead
- For production GKE, Dataplane V2 is preferred (kernel-level, lower overhead)

## References

- Istio docs: https://istio.io/latest/docs/
- Kiali: https://kiali.io/
- mTLS concepts: https://istio.io/latest/docs/concepts/security/#mutual-tls-authentication
