package com.baeldung.async.consumer;

import java.util.Map;
import java.util.Set;

import org.slf4j.MDC;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.domain.ProcessedEvent;
import com.baeldung.reactive.repository.OrderRepository;
import com.baeldung.reactive.repository.ProcessedEventRepository;

import lombok.extern.slf4j.Slf4j;
import reactor.core.publisher.Mono;

@Slf4j
@Service
public class OrderConsumer {

    private static final Map<OrderStatus, OrderStatus> NEXT_STATUS = Map.of(
        OrderStatus.INITIATION_SUCCESS, OrderStatus.RESERVE_INVENTORY,
        OrderStatus.INVENTORY_SUCCESS, OrderStatus.PREPARE_SHIPPING,
        OrderStatus.SHIPPING_FAILURE, OrderStatus.REVERT_INVENTORY);

    // Terminal saga failures with no further compensating transaction: the order
    // stays failed and nothing else in the saga will act on it, so they must be
    // logged at ERROR or they vanish silently (see ASSESSMENT.md tech debt #3).
    private static final Set<OrderStatus> UNRECOVERABLE_STATUSES = Set.of(
        OrderStatus.INVENTORY_FAILURE, OrderStatus.INVENTORY_REVERT_FAILURE);

    @Autowired
    private OrderRepository orderRepository;

    @Autowired
    private OrderProducer orderProducer;

    @Autowired
    private ProcessedEventRepository processedEventRepository;

    @KafkaListener(topics = "orders", groupId = "orders")
    public void consume(Order order) {
        String orderId = order.getId().toHexString();
        // Reactor's automatic context propagation (spring.reactor.context-propagation=auto,
        // #46) turned out NOT to carry MDC into .subscribe()'s callbacks - confirmed live by
        // deploying and grepping raw log output: setting MDC once here and calling .subscribe()
        // in scope still left "orderId" missing from every line the callbacks below emit.
        // Each callback below sets it again itself instead of relying on inheritance.
        try (var ignored = MDC.putCloseable("orderId", orderId)) {
            log.info("Order received to process: {}", order);
        }
        // Issue #48: dedup insert first, keyed on (orderId, status) - the Mongo status save
        // below is idempotent-by-value on its own, but a redelivered message's re-publish to
        // NEXT_STATUS is not (a redelivered INVENTORY_SUCCESS would double-fire
        // PREPARE_SHIPPING, a redelivered SHIPPING_FAILURE would double-fire REVERT_INVENTORY).
        String dedupId = orderId + ":" + order.getOrderStatus();
        processedEventRepository.insert(new ProcessedEvent(dedupId))
            .then(orderRepository.findById(order.getId())
                .map(o -> o.setOrderStatus(order.getOrderStatus())
                    .setResponseMessage(order.getResponseMessage()))
                .flatMap(orderRepository::save))
            .onErrorResume(DuplicateKeyException.class, e -> Mono.empty())
            .subscribe(
                saved -> {
                    try (var ignored = MDC.putCloseable("orderId", orderId)) {
                        if (saved == null) {
                            log.info("Duplicate {} event for order {}, already processed - skipping.",
                                order.getOrderStatus(), order.getId());
                            return;
                        }
                        if (UNRECOVERABLE_STATUSES.contains(order.getOrderStatus())) {
                            log.error("Order {} reached unrecoverable status {} with no compensating action: {}",
                                order.getId(), order.getOrderStatus(), order.getResponseMessage());
                        }
                        OrderStatus next = NEXT_STATUS.get(order.getOrderStatus());
                        if (next != null) {
                            orderProducer.sendMessage(saved.setOrderStatus(next));
                        }
                    }
                },
                err -> {
                    try (var ignored = MDC.putCloseable("orderId", orderId)) {
                        log.error("Failed to process order {} for status {}", order.getId(), order.getOrderStatus(), err);
                    }
                });
    }
}