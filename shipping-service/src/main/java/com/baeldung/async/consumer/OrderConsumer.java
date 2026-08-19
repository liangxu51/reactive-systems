package com.baeldung.async.consumer;

import java.io.IOException;

import org.slf4j.MDC;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.reactive.service.ShippingService;

import lombok.extern.slf4j.Slf4j;
import reactor.core.publisher.Mono;

@Slf4j
@Service
public class OrderConsumer {

    @Autowired
    ShippingService shippingService;

    @Autowired
    OrderProducer orderProducer;

    @KafkaListener(topics = "orders", groupId = "shipping")
    public void consume(Order order) throws IOException {
        String orderId = order.getId().toHexString();
        // Reactor's automatic context propagation (spring.reactor.context-propagation=auto,
        // #46) turned out NOT to carry MDC into doOnSuccess/doOnError/subscribe callbacks -
        // confirmed live by deploying and grepping raw log output: setting MDC once here left
        // "orderId" missing from every line those callbacks emit, even on a proper Reactor
        // operator (doOnSuccess), not just the terminal subscribe(). Each callback below sets
        // it again itself instead of relying on inheritance. This outer scope still covers the
        // "Order received" line and whatever handleOrder() itself logs while synchronously
        // building the Mono chain, before subscribe() hands off to Mongo's own driver threads.
        try (var ignored = MDC.putCloseable("orderId", orderId)) {
            log.info("Order received to process: {}", order);
            if (OrderStatus.PREPARE_SHIPPING.equals(order.getOrderStatus())) {
                shippingService.handleOrder(order)
                    // Issue #48: must sit before doOnSuccess/doOnError, not inside
                    // ShippingService's @Transactional method - see the comment on
                    // handleOrder there for why catching this inside the transaction
                    // breaks (Spring attempts to COMMIT an already-aborted transaction,
                    // which MongoDB rejects). By the time this operator runs, the
                    // transaction has already been rolled back cleanly.
                    .onErrorResume(DuplicateKeyException.class, e -> Mono.empty())
                    .doOnSuccess(o -> {
                        try (var ignored2 = MDC.putCloseable("orderId", orderId)) {
                            // Issue #48: Mono.empty() (o == null on a Mono<Order>'s
                            // doOnSuccess) means the dedup check in ShippingService
                            // caught a redelivered message - already handled, nothing
                            // to publish again.
                            if (o == null) {
                                log.info("Duplicate PREPARE_SHIPPING for order {}, already processed - skipping.", order.getId());
                                return;
                            }
                            log.info("Order processed succesfully.");
                            orderProducer.sendMessage(order.setOrderStatus(OrderStatus.SHIPPING_SUCCESS)
                                .setShippingDate(o.getShippingDate()));
                        }
                    })
                    .doOnError(e -> {
                        try (var ignored2 = MDC.putCloseable("orderId", orderId)) {
                            if (log.isErrorEnabled())
                                log.error("Order failed to process: " + e);
                            orderProducer.sendMessage(order.setOrderStatus(OrderStatus.SHIPPING_FAILURE)
                                .setResponseMessage(e.getMessage()));
                        }
                    })
                    .subscribe(o -> {
                    }, e -> {
                        try (var ignored2 = MDC.putCloseable("orderId", orderId)) {
                            log.error("Failed to process order {} for status {}", order.getId(), order.getOrderStatus(), e);
                        }
                    });
            }
        }
    }
}
