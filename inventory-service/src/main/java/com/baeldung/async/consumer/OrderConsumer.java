package com.baeldung.async.consumer;

import java.io.IOException;
import java.time.Duration;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.reactive.service.ProductService;
import com.mongodb.MongoException;

import lombok.extern.slf4j.Slf4j;
import reactor.util.retry.Retry;

@Slf4j
@Service
public class OrderConsumer {

    // Retrying re-subscribes to the @Transactional Mono, which starts a fresh Mongo transaction
    // per attempt - required so a retried stock check re-reads post-conflict stock instead of
    // reusing the aborted transaction. Only transient write conflicts are retried; genuine
    // business failures (e.g. out of stock) still propagate immediately.
    private static final Retry STOCK_CONFLICT_RETRY = Retry.backoff(3, Duration.ofMillis(50))
        .filter(OrderConsumer::isTransientStockConflict);

    @Autowired
    ProductService productService;

    @Autowired
    OrderProducer orderProducer;

    @KafkaListener(topics = "orders", groupId = "inventory")
    public void consume(Order order) throws IOException {
        log.info("Order received to process: {}", order);
        if (OrderStatus.RESERVE_INVENTORY.equals(order.getOrderStatus())) {
            productService.handleOrder(order)
                .retryWhen(STOCK_CONFLICT_RETRY)
                .doOnSuccess(o -> {
                    log.info("Order processed succesfully.");
                    orderProducer.sendMessage(order.setOrderStatus(OrderStatus.INVENTORY_SUCCESS));
                })
                .doOnError(e -> {
                    if (log.isDebugEnabled())
                        log.error("Order failed to process: " + e);
                    orderProducer.sendMessage(order.setOrderStatus(OrderStatus.INVENTORY_FAILURE)
                        .setResponseMessage(e.getMessage()));
                })
                .subscribe(o -> {
                }, e -> log.error("Failed to process order {} for status {}", order.getId(), order.getOrderStatus(), e));
        } else if (OrderStatus.REVERT_INVENTORY.equals(order.getOrderStatus())) {
            productService.revertOrder(order)
                .retryWhen(STOCK_CONFLICT_RETRY)
                .doOnSuccess(o -> {
                    log.info("Order reverted succesfully.");
                    orderProducer.sendMessage(order.setOrderStatus(OrderStatus.INVENTORY_REVERT_SUCCESS));
                })
                .doOnError(e -> {
                    if (log.isDebugEnabled())
                        log.error("Order failed to revert: " + e);
                    orderProducer.sendMessage(order.setOrderStatus(OrderStatus.INVENTORY_REVERT_FAILURE)
                        .setResponseMessage(e.getMessage()));
                })
                .subscribe(o -> {
                }, e -> log.error("Failed to revert order {} for status {}", order.getId(), order.getOrderStatus(), e));
        }
    }

    private static boolean isTransientStockConflict(Throwable throwable) {
        for (Throwable t = throwable; t != null; t = t.getCause()) {
            if (t instanceof MongoException mongoException
                && mongoException.hasErrorLabel(MongoException.TRANSIENT_TRANSACTION_ERROR_LABEL)) {
                return true;
            }
        }
        return false;
    }
}