# MongoDB TLS Configuration

This directory contains configuration and scripts for enabling TLS (Transport Layer Security) for MongoDB in this project.

## Overview

All traffic between Java services and MongoDB, as well as intra-replica-set communication, is now encrypted using TLS.

- **TLS Mode**: `preferTLS` — accepts both TLS and non-TLS connections (suitable for migration)
  - Can be upgraded to `requireTLS` in `docker-compose.yml` for stricter security
- **Certificates**: Self-signed for development (see below for production recommendations)

## For Local Development

### Generating Certificates

Certificates are stored in `mongodb-certs/` and are **NOT committed to git**. Generate them once:

```bash
./scripts/generate-mongodb-tls-certs.sh
```

This creates:
- `mongodb-certs/ca-cert.pem` — Self-signed CA certificate
- `mongodb-certs/server-combined.pem` — Server certificate + private key (used by MongoDB)

### Running with Docker Compose

```bash
docker-compose up --build
```

Docker automatically mounts the certificates and configures MongoDB with TLS.

### Running Services Locally (Outside Docker)

Services default to `mongodb://localhost:27017/...?tls=true&tlsAllowInvalidCertificates=true`

For development, `tlsAllowInvalidCertificates=true` allows self-signed certificates.

**To trust the CA certificate instead:**

1. Import the CA into your JVM keystore:
   ```bash
   keytool -import -alias mongodb-ca -file mongodb-certs/ca-cert.pem \
     -keystore $JAVA_HOME/lib/security/cacerts -storepass changeit
   ```

2. Remove `&tlsAllowInvalidCertificates=true` from `application.properties`

## For Production

**Do NOT use self-signed certificates.** Instead:

1. **Option A: cert-manager + Let's Encrypt (Kubernetes)**
   - Deploy cert-manager to your cluster
   - Create a Certificate resource for `mongo-db.your-domain.com`
   - Mount the issued certificate and CA in the MongoDB StatefulSet

2. **Option B: Internal CA (Enterprise)**
   - Generate a certificate signed by your internal Certificate Authority
   - Place the certificate, key, and CA files in MongoDB's data directory
   - Update `docker-compose.yml` and `application.properties`

3. **Then:**
   - Change `tlsMode` from `preferTLS` to `requireTLS` in `docker-compose.yml`
   - Remove `&tlsAllowInvalidCertificates=true` from all `application.properties`
   - Add `&tlsDisableOCSPEndpointCheck=true` if behind a proxy that doesn't support OCSP

## TLS Connection Parameters

All services use MongoDB connection strings with:

```
mongodb://localhost:27017/reactive-systems?tls=true&tlsAllowInvalidCertificates=true&retryWrites=false
```

**Parameters:**
- `tls=true` — Enable TLS for client-MongoDB communication
- `tlsAllowInvalidCertificates=true` — **Development only**: allow self-signed certs
- `retryWrites=false` — Disable automatic retry (not supported on single replica set during initialization)

## Verifying TLS is Working

From inside a MongoDB container:

```bash
docker exec -it mongo-db mongo --ssl --sslCAFile /etc/mongod-tls/ca-cert.pem
```

From a Java service (check logs for TLS handshake messages):

```bash
docker logs -f order-service | grep -i tls
```

## References

- [MongoDB TLS/SSL Configuration](https://docs.mongodb.com/manual/tutorial/configure-ssl/)
- [Spring Data MongoDB SSL Support](https://docs.spring.io/spring-data/mongodb/docs/current/reference/html/#mongo.tls)
- [Java SSL/TLS Debugging](https://docs.oracle.com/javase/8/docs/technotes/guides/security/jsse/ReadDebug.html)
