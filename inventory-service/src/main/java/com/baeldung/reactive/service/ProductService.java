package com.baeldung.reactive.service;

import java.util.stream.Collectors;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.domain.Product;
import com.baeldung.domain.ProcessedEvent;
import com.baeldung.reactive.repository.ProcessedEventRepository;
import com.baeldung.reactive.repository.ProductRepository;

import lombok.extern.slf4j.Slf4j;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;

@Slf4j
@Service
public class ProductService {

    @Autowired
    ProductRepository productRepository;

    @Autowired
    ProcessedEventRepository processedEventRepository;

    // Issue #48: inserted first, inside the same transaction as the stock
    // write below - a redelivered message hits the unique _id index and
    // throws DuplicateKeyException before ever touching stock, and a
    // transient-conflict retry (STOCK_CONFLICT_RETRY in OrderConsumer)
    // rolls this insert back along with everything else in the aborted
    // transaction, so a genuine retry still re-attempts it fresh.
    private Mono<Void> markProcessedOrEmpty(Order order) {
        String dedupId = order.getId().toHexString() + ":" + order.getOrderStatus();
        return processedEventRepository.insert(new ProcessedEvent(dedupId)).then();
    }

    @Transactional
    public Mono<Order> handleOrder(Order order) {
        log.info("Handle order invoked with: {}", order);
        return markProcessedOrEmpty(order)
            .then(Flux.fromIterable(order.getLineItems())
                .flatMap(l -> productRepository.findById(l.getProductId()))
                .flatMap(p -> {
                    int q = order.getLineItems()
                        .stream()
                        .filter(l -> l.getProductId().equals(p.getId()))
                        .findAny()
                        .get()
                        .getQuantity();
                    // SEC-101: Kafka publishes to `orders` are unauthenticated (tracked
                    // separately as issue #42 - fixing that needs live-cluster SASL
                    // testing, not a blind config patch). Until then, a forged message
                    // with a non-positive quantity must not reach the stock math below -
                    // `stock - (negative q)` would silently increase stock instead of
                    // reserving it.
                    if (q <= 0) {
                        return Mono.error(new IllegalArgumentException("Invalid quantity for product " + p.getId() + ": " + q));
                    }
                    if (p.getStock() >= q) {
                        p.setStock(p.getStock() - q);
                        return productRepository.save(p);
                    } else {
                        return Mono.error(new RuntimeException("Product is out of stock: " + p.getId()));
                    }
                })
                .then(Mono.just(order.setOrderStatus(OrderStatus.SUCCESS))));
        // DuplicateKeyException from markProcessedOrEmpty deliberately propagates out of
        // this @Transactional method uncaught - Spring's reactive transaction interceptor
        // only rolls back on an error signal. Catching it here and completing empty instead
        // would make the method return successfully, and Spring would then try to COMMIT a
        // transaction that already had a failed write in it, which MongoDB rejects (and
        // rejects with a transient-transaction-error label, which STOCK_CONFLICT_RETRY below
        // would then retry 3 times for no reason before failing for real - confirmed live).
        // The dedup check in OrderConsumer catches this same DuplicateKeyException *after*
        // this method returns, once the transaction has already been safely rolled back.
    }

    @Transactional
    public Mono<Order> revertOrder(Order order) {
        log.info("Revert order invoked with: {}", order);
        return markProcessedOrEmpty(order)
            .then(Flux.fromIterable(order.getLineItems())
                .flatMap(l -> productRepository.findById(l.getProductId()))
                .flatMap(p -> {
                    int q = order.getLineItems()
                        .stream()
                        .filter(l -> l.getProductId().equals(p.getId()))
                        .collect(Collectors.toList())
                        .get(0)
                        .getQuantity();

                    // SEC-101: same non-positive-quantity guard as handleOrder - without
                    // it, a forged revert message with a negative quantity decreases
                    // stock instead of restoring it.
                    if (q <= 0) {
                        return Mono.error(new IllegalArgumentException("Invalid quantity for product " + p.getId() + ": " + q));
                    }
                    p.setStock(p.getStock() + q);
                    return productRepository.save(p);
                })
                .then(Mono.just(order.setOrderStatus(OrderStatus.SUCCESS))));
        // See the matching comment in handleOrder above - DuplicateKeyException must
        // propagate out of this @Transactional method uncaught, not be resumed here.
    }

    public Flux<Product> getProducts() {
        return productRepository.findAll();
    }

}
