#!/bin/bash
# Generate self-signed TLS certificates for MongoDB development
# This script creates a self-signed CA and server certificate for MongoDB

set -e

CERTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/mongodb-certs"
mkdir -p "$CERTS_DIR"

echo "Generating MongoDB TLS certificates in $CERTS_DIR..."

# Generate private key for CA
openssl genrsa -out "$CERTS_DIR/ca-key.pem" 4096

# Generate self-signed CA certificate (valid for 10 years)
openssl req -new -x509 -days 3650 -key "$CERTS_DIR/ca-key.pem" -out "$CERTS_DIR/ca-cert.pem" \
  -subj "/CN=MongoDB-CA/O=Baeldung/C=US"

# Generate private key for server
openssl genrsa -out "$CERTS_DIR/server-key.pem" 4096

# Generate certificate signing request for server
openssl req -new -key "$CERTS_DIR/server-key.pem" -out "$CERTS_DIR/server.csr" \
  -subj "/CN=mongo-db/O=Baeldung/C=US"

# Sign server certificate with CA (valid for 10 years)
openssl x509 -req -days 3650 -in "$CERTS_DIR/server.csr" \
  -CA "$CERTS_DIR/ca-cert.pem" -CAkey "$CERTS_DIR/ca-key.pem" -CAcreateserial \
  -out "$CERTS_DIR/server-cert.pem"

# Create combined PEM file (certificate + key) for MongoDB
cat "$CERTS_DIR/server-cert.pem" "$CERTS_DIR/server-key.pem" > "$CERTS_DIR/server-combined.pem"

# Set appropriate permissions (MongoDB requires restrictive permissions)
chmod 400 "$CERTS_DIR/server-combined.pem"
chmod 444 "$CERTS_DIR/ca-cert.pem"

# Clean up temporary files
rm -f "$CERTS_DIR/server.csr" "$CERTS_DIR/ca-key.pem"

echo "✓ MongoDB TLS certificates generated successfully:"
echo "  - CA certificate: $CERTS_DIR/ca-cert.pem"
echo "  - Server certificate + key: $CERTS_DIR/server-combined.pem"
echo ""
echo "For local development, these self-signed certificates are suitable."
echo "For production, use certificates from a trusted Certificate Authority."
