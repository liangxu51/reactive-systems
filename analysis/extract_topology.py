#!/usr/bin/env python3
"""
Extract a dependency/topology map for the reactive-systems repo.

Scope: order-service, order-service-vt, inventory-service, shipping-service
(Java/Spring), frontend (React/Vite). No mainframe/JCL/CICS constructs apply
here; the three "edges live in two places" principles are adapted as:

  1. Direct calls = Java field/type references between Controller/Service/
     Repository/Consumer/Producer classes (regex on simple class name usage).
     Dispatcher calls = Kafka topic pub/sub (@KafkaListener / kafkaTemplate.send)
     — these have no direct source-level caller, only a topic subscription,
     so they are modeled as "dispatch" edges through a datastore node.
  2. Code<->storage join = Spring Data `@Document` (Mongo collection, default
     name = lowercase simple class name) and the Kafka topic string literal
     used by both `@KafkaListener(topics=...)` and `kafkaTemplate.send(...)`.
  3. Entry points = `@RestController` request-mapped methods, `@KafkaListener`
     methods, and `@SpringBootApplication` main classes — all discoverable
     from annotations in source, no external deployment descriptor exists
     in this repo (docker-compose/Helm only wire ports/env, not entry points).

Run: python3 analysis/extract_topology.py
Writes: analysis/topology.json, prints a human summary.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "analysis" / "topology.json"

JAVA_SERVICES = {
    "order-service": "Order Orchestration (reactive)",
    "order-service-vt": "Order Orchestration (virtual-thread variant)",
    "inventory-service": "Inventory / Stock Reservation",
    "shipping-service": "Shipping / Fulfillment",
}

# Simple-class-name -> role, decided by directory/filename convention observed
# across all four services (controller/, service/, repository/, consumer/,
# producer/). Domain/DTO classes (Order, Product, Shipment, LineItem, Address,
# OrderStatus, *Serializer) are deliberately excluded as graph nodes: every
# module references them, so including them would produce a hairball without
# adding topology information (see observations).
ROLE_BY_DIR = {
    "controller": "controller",
    "service": "service",
    "repository": "repository",
    "consumer": "consumer",
    "producer": "producer",
}

DOMAIN_MODEL_NAMES = {
    "Order", "LineItem", "Address", "Product", "Shipment", "OrderStatus",
    "ObjectIdSerializer", "ObjectIdValueSerializer",
}


def loc(path: Path) -> int:
    return sum(1 for _ in path.open(encoding="utf-8", errors="replace"))


def find_java_modules(service_dir: Path, service: str):
    """Return {simple_class_name: {id, file, loc, role, is_entry, kafka_topics, doc_collection}}"""
    modules = {}
    src = service_dir / "src" / "main" / "java"
    for f in src.rglob("*.java"):
        role = None
        for part in f.parts:
            if part in ROLE_BY_DIR:
                role = ROLE_BY_DIR[part]
                break
        text = f.read_text(encoding="utf-8", errors="replace")
        m = re.search(r"\bclass\s+(\w+)", text)
        if not m:
            m = re.search(r"\binterface\s+(\w+)", text)
        if not m:
            continue
        cname = m.group(1)
        if cname in DOMAIN_MODEL_NAMES:
            continue  # excluded per module-scope decision above
        is_app_main = "@SpringBootApplication" in text
        if role is None and not is_app_main:
            continue  # skip config/exception-handler odds and ends with no role
        node_id = f"{service}.{cname}"
        modules[cname] = {
            "id": node_id,
            "name": cname,
            "kind": "module",
            "language": "java",
            "loc": loc(f),
            "file": str(f.relative_to(ROOT)),
            "role": role or "application",
            "is_app_main": is_app_main,
            "text": text,
        }
    return modules


def find_frontend_modules(fe_src: Path):
    modules = {}
    for f in fe_src.rglob("*.jsx"):
        name = f.stem
        text = f.read_text(encoding="utf-8", errors="replace")
        modules[name] = {
            "id": f"frontend.{name}",
            "name": name,
            "kind": "module",
            "language": "jsx",
            "loc": loc(f),
            "file": str(f.relative_to(ROOT)),
            "role": "ui",
            "is_app_main": name == "main",
            "text": text,
        }
    for f in fe_src.rglob("*.js"):
        name = f.stem
        text = f.read_text(encoding="utf-8", errors="replace")
        modules[name] = {
            "id": f"frontend.{name}",
            "name": name,
            "kind": "module",
            "language": "js",
            "loc": loc(f),
            "file": str(f.relative_to(ROOT)),
            "role": "api-client" if "api" in str(f) else "hook",
            "is_app_main": False,
            "text": text,
        }
    return modules


def main():
    all_modules = {}   # node_id -> module dict
    by_service = {}     # service -> {simple_name: module dict}
    edges = []
    entry_points = []
    observations = []

    # --- Java services ---
    for service in JAVA_SERVICES:
        sdir = ROOT / service
        mods = find_java_modules(sdir, service)
        by_service[service] = mods
        for m in mods.values():
            all_modules[m["id"]] = m

    # --- Frontend ---
    fe_mods = find_frontend_modules(ROOT / "frontend" / "src")
    by_service["frontend"] = fe_mods
    for m in fe_mods.values():
        all_modules[m["id"]] = m

    # --- Datastores ---
    datastores = {
        "ds:orders-topic": {"id": "ds:orders-topic", "name": "orders (Kafka topic)", "kind": "datastore"},
        "ds:order-collection": {"id": "ds:order-collection", "name": "order (Mongo collection)", "kind": "datastore"},
        "ds:product-collection": {"id": "ds:product-collection", "name": "product (Mongo collection)", "kind": "datastore"},
        "ds:shipment-collection": {"id": "ds:shipment-collection", "name": "shipment (Mongo collection)", "kind": "datastore"},
    }
    # order-service-vt shares the SAME Mongo collection + Kafka topic as order-service
    # (confirmed in ASSESSMENT.md — mutually-exclusive deploy, but the physical
    # datastore binding is identical). This is intentionally a single shared node,
    # not two, to make that coupling visible in the map.

    # --- 1. Direct call edges: intra-service class-name references ---
    for service, mods in by_service.items():
        if service == "frontend":
            continue
        names = list(mods.keys())
        for cname, m in mods.items():
            for other in names:
                if other == cname:
                    continue
                # word-boundary match of the other class's simple name in this file
                if re.search(rf"\b{re.escape(other)}\b", m["text"]):
                    edges.append({"source": m["id"], "target": mods[other]["id"], "kind": "call"})

    # --- 2. Kafka dispatch/write edges ---
    for service, mods in by_service.items():
        if service == "frontend":
            continue
        for cname, m in mods.items():
            text = m["text"]
            if m["role"] == "consumer" and re.search(r'@KafkaListener\(topics\s*=\s*"orders"', text):
                edges.append({"source": "ds:orders-topic", "target": m["id"], "kind": "dispatch"})
                entry_points.append(m["id"])
            if m["role"] == "producer" and re.search(r'kafkaTemplate\.send\("orders"', text):
                edges.append({"source": m["id"], "target": "ds:orders-topic", "kind": "write"})

    # --- 3. Mongo read/write edges (Repository -> @Document collection) ---
    doc_collection_by_service = {
        "order-service": ("OrderRepository", "ds:order-collection"),
        "order-service-vt": ("OrderRepository", "ds:order-collection"),
        "inventory-service": ("ProductRepository", "ds:product-collection"),
        "shipping-service": ("ShipmentRepository", "ds:shipment-collection"),
    }
    for service, (repo_name, ds_id) in doc_collection_by_service.items():
        mods = by_service[service]
        if repo_name in mods:
            repo_id = mods[repo_name]["id"]
            edges.append({"source": repo_id, "target": ds_id, "kind": "read"})
            edges.append({"source": repo_id, "target": ds_id, "kind": "write"})

    # --- 4. Entry points: REST controllers + Spring Boot main classes ---
    for service, mods in by_service.items():
        if service == "frontend":
            continue
        for cname, m in mods.items():
            if m["role"] == "controller" and "@RestController" in m["text"]:
                entry_points.append(m["id"])
            if m["is_app_main"]:
                entry_points.append(m["id"])
            # @ControllerAdvice classes are framework-dispatch targets (Spring routes
            # to them on unhandled exceptions from any controller in the same service)
            # — no source-level caller exists, same "dispatcher" caveat as Kafka topics.
            if "@ControllerAdvice" in m["text"]:
                entry_points.append(m["id"])
                observations.append(
                    f"{m['id']} is a @ControllerAdvice — Spring dispatches to it on any unhandled "
                    "exception from a controller in the same service. No static call edge exists; "
                    "treated as an entry point rather than a dead end for that reason."
                )

    # frontend main.jsx is the browser entry point
    if "main" in fe_mods:
        entry_points.append(fe_mods["main"]["id"])

    # --- 5. Frontend -> backend controller call edges (hardcoded fetch URLs) ---
    url_targets = [
        (r"localhost:8080/api/orders", by_service["order-service"].get("OrderController")),
        (r"localhost:8081/api/products", by_service["inventory-service"].get("ProductController")),
    ]
    for fname, m in fe_mods.items():
        for pattern, target_mod in url_targets:
            if target_mod and re.search(pattern, m["text"]):
                edges.append({"source": m["id"], "target": target_mod["id"], "kind": "call"})

    # frontend internal composition edges (App.jsx uses the hook/components)
    if "App" in fe_mods:
        app_text = fe_mods["App"]["text"]
        for other in ("OrderForm", "OrderList", "useOrderStream"):
            if other in fe_mods and re.search(rf"\b{other}\b", app_text):
                edges.append({"source": fe_mods["App"]["id"], "target": fe_mods[other]["id"], "kind": "call"})
        # App.jsx is the actual caller of every ordersApi function (OrderForm/OrderList
        # are presentational and receive data via props/callbacks, confirmed by reading
        # both files — they contain no ordersApi import).
        if "ordersApi" in fe_mods and re.search(r"from '\./api/ordersApi'", app_text):
            edges.append({"source": fe_mods["App"]["id"], "target": fe_mods["ordersApi"]["id"], "kind": "call"})
    if "main" in fe_mods and "App" in fe_mods:
        edges.append({"source": fe_mods["main"]["id"], "target": fe_mods["App"]["id"], "kind": "call"})

    # dedupe edges
    seen = set()
    deduped = []
    for e in edges:
        key = (e["source"], e["target"], e["kind"])
        if key not in seen:
            seen.add(key)
            deduped.append(e)
    edges = deduped

    # --- Dead-end candidates: modules with zero inbound edges, excluding entry points ---
    inbound = {m_id: 0 for m_id in all_modules}
    for e in edges:
        if e["target"] in inbound:
            inbound[e["target"]] += 1
    entry_set = set(entry_points)
    dead_ends = [m_id for m_id, cnt in inbound.items() if cnt == 0 and m_id not in entry_set]

    # --- Build tree ---
    def module_leaf(m):
        return {"id": m["id"], "name": m["name"], "kind": "module", "language": m["language"],
                "loc": m["loc"], "file": m["file"]}

    domain_children = []
    for service, display in JAVA_SERVICES.items():
        mods = by_service[service]
        domain_children.append({
            "id": f"dom:{service}", "name": display, "kind": "domain",
            "children": [module_leaf(m) for m in mods.values()],
        })
    domain_children.append({
        "id": "dom:frontend", "name": "Frontend (React/Vite UI)", "kind": "domain",
        "children": [module_leaf(m) for m in fe_mods.values()],
    })
    domain_children.append({
        "id": "dom:data", "name": "Data stores", "kind": "domain",
        "children": list(datastores.values()),
    })

    root = {"id": "sys", "name": "reactive-systems", "kind": "system", "children": domain_children}

    # --- Observations ---
    observations.append(
        "order-service and order-service-vt bind the SAME Kafka topic ('orders') and the SAME Mongo "
        "'order' collection under different consumer group IDs ('orders' vs 'orders-vt') — modeled here "
        "as edges into the identical ds:orders-topic / ds:order-collection nodes. They are deploy-time "
        "mutually exclusive (Helm chart enforces this), but nothing at the code or infra level stops both "
        "running at once in an ad hoc local setup, which would double-process every order."
    )
    observations.append(
        "The 'orders' Kafka topic is a single point of failure and single point of coupling for the whole "
        "saga: all four consumer/producer pairs across every service read and write the same topic with no "
        "per-stage partitioning or routing key, visible in the map as one datastore node with the highest "
        "edge count by far."
    )
    observations.append(
        "inventory-service publishes INVENTORY_FAILURE and INVENTORY_REVERT_FAILURE onto the orders topic, "
        "but no consumer in the graph has an inbound dispatch edge from those specific statuses — order-service's "
        "consumer only branches on INITIATION_SUCCESS, INVENTORY_SUCCESS, and SHIPPING_FAILURE. This isn't a "
        "topology extraction gap; it's a genuine missing edge in the saga's own state machine (see ASSESSMENT.md "
        "Technical Debt #3) and is surfaced explicitly in the 'stalled order' persona flow below."
    )
    observations.append(
        "shipping-service has no @RestController and therefore no HTTP entry point at all — its only entry "
        "point is the Kafka dispatch edge from ds:orders-topic. A grep-only/HTTP-only topology extraction "
        "would have marked all of shipping-service's classes as dead code; they are not."
    )
    observations.append(
        "frontend hardcodes 'localhost:8080'/'localhost:8081' as call targets rather than resolving them "
        "through any config/env layer, so the frontend->controller edges in this map are accurate for local "
        "dev but do not hold in the Helm/k8s deployment this same repo ships (ASSESSMENT.md Technical Debt #4)."
    )
    observations.append(
        "Domain/DTO classes (Order, Product, Shipment, LineItem, Address, OrderStatus, the two ObjectId "
        "serializers) were deliberately excluded as graph nodes — every module in every service references "
        "them, so including them would turn the map into a hairball without adding topology information. "
        "Their duplication across services is already documented in ASSESSMENT.md and CLAUDE.md."
    )

    # --- Persona flows ---
    oc = by_service["order-service"]
    ic = by_service["inventory-service"]
    sc = by_service["shipping-service"]
    fe = fe_mods

    flows = [
        {
            "name": "Customer places an order and it ships",
            "persona": "Customer",
            "description": "A customer submits an order through the storefront, stock is reserved, and a shipment is created — the saga's happy path.",
            "steps": [
                {"label": "Customer submits the order form", "nodes": [fe["OrderForm"]["id"], fe["ordersApi"]["id"]]},
                {"label": "Order service saves the order and announces it", "nodes": [oc["OrderController"]["id"], oc["OrderService"]["id"], oc["OrderRepository"]["id"], "ds:order-collection", oc["OrderProducer"]["id"], "ds:orders-topic"]},
                {"label": "Inventory service reserves stock for the order", "nodes": ["ds:orders-topic", ic["OrderConsumer"]["id"], ic["ProductService"]["id"], ic["ProductRepository"]["id"], "ds:product-collection", ic["OrderProducer"]["id"]]},
                {"label": "Order service tells shipping to prepare the parcel", "nodes": ["ds:orders-topic", oc["OrderConsumer"]["id"], oc["OrderProducer"]["id"]]},
                {"label": "Shipping service creates the shipment record", "nodes": ["ds:orders-topic", sc["OrderConsumer"]["id"], sc["ShippingService"]["id"], sc["ShipmentRepository"]["id"], "ds:shipment-collection"]},
                {"label": "Customer watches order status update live", "nodes": [fe["useOrderStream"]["id"], oc["OrderController"]["id"]]},
            ],
        },
        {
            "name": "Order placed outside shipping hours triggers a stock rollback",
            "persona": "Customer",
            "description": "A customer orders after 18:00; shipping refuses the order and the saga automatically gives the reserved stock back.",
            "steps": [
                {"label": "Order and inventory reservation succeed as normal", "nodes": [oc["OrderController"]["id"], "ds:orders-topic", ic["OrderConsumer"]["id"], ic["ProductService"]["id"]]},
                {"label": "Shipping rejects the order — outside the 10:00–18:00 acceptance window", "nodes": ["ds:orders-topic", sc["OrderConsumer"]["id"], sc["ShippingService"]["id"], sc["OrderProducer"]["id"]]},
                {"label": "Order service sees SHIPPING_FAILURE and asks inventory to revert", "nodes": ["ds:orders-topic", oc["OrderConsumer"]["id"], oc["OrderProducer"]["id"]]},
                {"label": "Inventory service restores the stock it had reserved", "nodes": ["ds:orders-topic", ic["OrderConsumer"]["id"], ic["ProductService"]["id"], "ds:product-collection"]},
            ],
        },
        {
            "name": "Customer's order silently stalls when stock is insufficient",
            "persona": "Customer",
            "description": "A customer orders more of a product than is in stock; inventory correctly refuses the reservation, but nothing downstream ever tells the customer or retries — the order just stops.",
            "steps": [
                {"label": "Order is accepted and announced", "nodes": [oc["OrderController"]["id"], "ds:orders-topic"]},
                {"label": "Inventory service checks stock and finds it insufficient", "nodes": ["ds:orders-topic", ic["OrderConsumer"]["id"], ic["ProductService"]["id"], "ds:product-collection"]},
                {"label": "Inventory publishes INVENTORY_FAILURE onto the shared topic", "nodes": [ic["OrderProducer"]["id"], "ds:orders-topic"]},
                {"label": "No consumer in the system is listening for INVENTORY_FAILURE — the order status never advances again", "nodes": ["ds:orders-topic"]},
            ],
        },
    ]

    system = {
        "system": "reactive-systems",
        "root": root,
        "edges": edges,
        "entryPoints": sorted(set(entry_points)),
        "deadEnds": sorted(set(dead_ends)),
        "observations": observations,
        "flows": flows,
    }

    OUT.write_text(json.dumps(system, indent=2))

    # --- Human summary ---
    print(f"Modules: {len(all_modules)}  Datastores: {len(datastores)}  Edges: {len(edges)}")
    print(f"Entry points ({len(set(entry_points))}):")
    for e in sorted(set(entry_points)):
        print(f"  - {e}")
    print(f"Dead-end candidates ({len(dead_ends)}):")
    for d in dead_ends:
        print(f"  - {d}")
    print(f"Observations: {len(observations)}")
    print(f"Flows: {len(flows)}")
    print(f"Wrote {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
