package com.baeldung.domain;

import java.time.Instant;

import org.springframework.data.annotation.Id;
import org.springframework.data.mongodb.core.index.Indexed;
import org.springframework.data.mongodb.core.mapping.Document;

import lombok.Data;

// Issue #48: Kafka's at-least-once delivery means this service's @KafkaListener
// can see the same (orderId, status) pair more than once (consumer restart
// mid-batch, rebalance, etc.) - inserting one of these first, keyed on that
// pair, turns a redelivered message into a no-op instead of double-applying
// its side effect (a redelivered PREPARE_SHIPPING creating a second shipment
// with no link back to the first - Shipment has no orderId field). The id IS
// the dedup key - MongoDB's built-in unique _id index enforces "exactly one
// processed marker per (order, status)" with no extra index to declare or
// maintain.
// Explicit collection name, not the @Document default (which is just the
// decapitalized class name, "processedEvent") - all four services share one
// physical MongoDB database, and every service's ProcessedEvent class has
// the exact same simple name. Without this, this service's dedup marker
// would land in the SAME collection as e.g. order-service's marker for the
// same (orderId, status) key, causing spurious "duplicate" false positives
// on a message's genuine first delivery (confirmed live).
@Data
@Document("shipping_processed_event")
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
