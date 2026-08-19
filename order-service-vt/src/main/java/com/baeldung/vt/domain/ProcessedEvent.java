package com.baeldung.vt.domain;

import java.time.Instant;

import org.springframework.data.annotation.Id;
import org.springframework.data.mongodb.core.index.Indexed;
import org.springframework.data.mongodb.core.mapping.Document;

import lombok.Data;

// Issue #48: Kafka's at-least-once delivery means this service's @KafkaListener
// can see the same (orderId, status) pair more than once (consumer restart
// mid-batch, rebalance, etc.) - inserting one of these first, keyed on that
// pair, turns a redelivered message into a no-op instead of double-publishing
// the next saga step (e.g. a redelivered INVENTORY_SUCCESS re-triggering a
// second PREPARE_SHIPPING, or a redelivered SHIPPING_FAILURE double-firing
// REVERT_INVENTORY). The id IS the dedup key - MongoDB's built-in unique _id
// index enforces "exactly one processed marker per (order, status)" with no
// extra index to declare or maintain.
// Explicit collection name, not the @Document default (which is just the
// decapitalized class name, "processedEvent") - all four services share one
// physical MongoDB database, and every service's ProcessedEvent class has
// the exact same simple name. Without this, this service's dedup marker
// would land in the SAME collection as e.g. order-service's marker for the
// same (orderId, status) key, causing spurious "duplicate" false positives
// on a message's genuine first delivery (confirmed live against the
// reactive order-service - order-service and order-service-vt are mutually
// exclusive deployments, but kept distinct here too rather than relying on
// that).
@Data
@Document("order_vt_processed_event")
public class ProcessedEvent {

    @Id
    private String id;

    // TTL index (spring.data.mongodb.auto-index-creation=true, see
    // application.properties) - matches the 7-day retention precedent used
    // elsewhere in this repo (Loki logs, Mongo backups) rather than picking
    // an arbitrary new number. Redelivery can't plausibly happen once the
    // Kafka broker itself has expired the message, so markers don't need to
    // outlive that.
    @Indexed(expireAfterSeconds = 604800)
    private Instant processedAt = Instant.now();

    public ProcessedEvent() {
    }

    public ProcessedEvent(String id) {
        this.id = id;
    }

}
