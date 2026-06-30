# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a tutorial project for Baeldung demonstrating reactive systems in Java. It implements a full-stack e-commerce order processing pipeline using event-driven microservices. **Do not add unrelated code to this module** — it exists solely to illustrate the reactive systems concepts described in the companion article.

## Services

| Service | Port | Description |
|---|---|---|
| `order-service` | 8080 | REST API entry point; persists orders and orchestrates the workflow |
| `inventory-service` | 8081 | Reserves or reverts product stock |
| `shipping-service` | 8082 | Creates shipment records (only accepts orders between 10:00–18:00) |
| `frontend` | 80 (Docker) / 4200 (dev) | Angular 9 UI showing reactive vs. blocking order streaming |

## Build & Run Commands

### Java services (Maven multi-module, Java 21, Spring Boot 3)

```bash
# Build all three services from the root
mvn clean package -pl order-service,inventory-service,shipping-service

# Build a single service
mvn clean package -pl order-service

# Run a single service (requires MongoDB + Kafka running first)
mvn spring-boot:run -pl order-service
```

### Frontend (Angular 9)

```bash
cd frontend
npm install
npm start        # dev server at http://localhost:4200
npm test         # Karma/Jasmine unit tests
npm run build    # production build
```

### Run everything with Docker Compose

```bash
docker-compose up --build
```

This starts: Zookeeper, Kafka (port 29092), MongoDB (port 27017), all three Java services, and the nginx-served frontend.

## Infrastructure Dependencies

All three Java services require:
- **MongoDB** at `mongodb://localhost:27017/reactive-systems` — same database, separate collections
- **Kafka** at `localhost:9092` — single topic named `orders`

When running locally without Docker, start these before the services.

## Architecture: Event-Driven Order Workflow

All inter-service communication flows through a single Kafka topic (`orders`). Each service listens with its own consumer group and reacts to specific `OrderStatus` values:

```
Frontend → POST /api/orders
    → order-service saves order, publishes INITIATION_SUCCESS
        → order-service consumer sees INITIATION_SUCCESS → publishes RESERVE_INVENTORY
        → inventory-service consumer sees RESERVE_INVENTORY → reserves stock → publishes INVENTORY_SUCCESS or INVENTORY_FAILURE
            → order-service consumer sees INVENTORY_SUCCESS → publishes PREPARE_SHIPPING
            → shipping-service consumer sees PREPARE_SHIPPING → creates shipment → publishes SHIPPING_SUCCESS or SHIPPING_FAILURE
                → order-service consumer sees SHIPPING_FAILURE → publishes REVERT_INVENTORY
                → inventory-service consumer sees REVERT_INVENTORY → restores stock → publishes INVENTORY_REVERT_SUCCESS
```

The `OrderStatus` enum in each service (e.g., `order-service/.../constants/OrderStatus.java`) is the shared contract — all three copies must stay in sync.

## Key Design Patterns

- **Reactive stack**: Spring WebFlux + Project Reactor (`Mono`/`Flux`) + Spring Data MongoDB Reactive across all three services.
- **Kafka consumers** (`async/consumer/OrderConsumer`) use `@KafkaListener` and call `.subscribe()` explicitly since the reactive chain is not automatically subscribed inside a Kafka listener.
- **Saga / compensating transactions**: if shipping fails, `order-service` publishes `REVERT_INVENTORY` so `inventory-service` can roll back stock.
- **Domain duplication**: `Order`, `LineItem`, `Address`, `OrderStatus`, and `ObjectIdSerializer` are intentionally duplicated across all three services to keep them deployable independently.

## Frontend: Reactive vs. Blocking Demo

The Angular app has two services side-by-side:
- `orders-reactive.service.ts` — uses `EventSource` (SSE) to stream orders reactively from `GET /api/orders` as a `Flux<Order>` with `text/event-stream`.
- `orders-blocking.service.ts` — uses `HttpClient.get()` to fetch the full list at once.

The `OrderController` exposes the same `GET /api/orders` endpoint; Spring WebFlux automatically handles SSE when the client sends `Accept: text/event-stream`.
