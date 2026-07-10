package com.baeldung.vt.service;

import java.util.List;
import java.util.stream.Collectors;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.producer.OrderProducer;
import com.baeldung.vt.repository.OrderRepository;

import lombok.extern.slf4j.Slf4j;

@Slf4j
@Service
public class OrderService {

    @Autowired
    private OrderRepository orderRepository;

    @Autowired
    private OrderProducer orderProducer;

    public Order createOrder(Order order) {
        log.info("Create order invoked with: {}", order);
        Order saved;
        try {
            order.setLineItems(order.getLineItems()
                .stream()
                .filter(l -> l.getQuantity() > 0)
                .collect(Collectors.toList()));
            Order persisted = orderRepository.save(order);
            saved = orderRepository.save(persisted.setOrderStatus(OrderStatus.INITIATION_SUCCESS));
        } catch (Exception e) {
            return orderRepository.save(order
                .setOrderStatus(OrderStatus.FAILURE)
                .setResponseMessage(e.getMessage()));
        }

        // The order is now durably INITIATION_SUCCESS in the database. A failure to
        // publish here must not downgrade that already-committed state back to FAILURE,
        // otherwise we'd reintroduce the same dual-write divergence bug in reverse.
        try {
            orderProducer.sendMessage(saved);
        } catch (Exception e) {
            log.error("Order {} was persisted as INITIATION_SUCCESS but publishing to Kafka failed: {}",
                saved.getId(), e.getMessage(), e);
        }
        return saved;
    }

    public List<Order> getOrders() {
        log.info("Get all orders invoked.");
        return orderRepository.findAll();
    }

}
