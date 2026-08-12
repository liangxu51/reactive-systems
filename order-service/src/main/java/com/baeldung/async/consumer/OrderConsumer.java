package com.baeldung.async.consumer;

import java.util.Map;
import java.util.Set;

import org.slf4j.MDC;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.reactive.repository.OrderRepository;

import lombok.extern.slf4j.Slf4j;

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
        orderRepository.findById(order.getId())
            .map(o -> o.setOrderStatus(order.getOrderStatus())
                .setResponseMessage(order.getResponseMessage()))
            .flatMap(orderRepository::save)
            .subscribe(
                saved -> {
                    try (var ignored = MDC.putCloseable("orderId", orderId)) {
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