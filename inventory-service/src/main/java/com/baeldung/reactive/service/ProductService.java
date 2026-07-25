package com.baeldung.reactive.service;

import java.util.stream.Collectors;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.domain.Product;
import com.baeldung.reactive.repository.ProductRepository;

import lombok.extern.slf4j.Slf4j;
import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;

@Slf4j
@Service
public class ProductService {

    @Autowired
    ProductRepository productRepository;

    @Transactional
    public Mono<Order> handleOrder(Order order) {
        log.info("Handle order invoked with: {}", order);
        return Flux.fromIterable(order.getLineItems())
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
            .then(Mono.just(order.setOrderStatus(OrderStatus.SUCCESS)));
    }

    @Transactional
    public Mono<Order> revertOrder(Order order) {
        log.info("Revert order invoked with: {}", order);
        return Flux.fromIterable(order.getLineItems())
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
            .then(Mono.just(order.setOrderStatus(OrderStatus.SUCCESS)));
    }

    public Flux<Product> getProducts() {
        return productRepository.findAll();
    }

}
