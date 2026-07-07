package com.baeldung.async.consumer;

import java.util.Map;

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

    @Autowired
    private OrderRepository orderRepository;

    @Autowired
    private OrderProducer orderProducer;

    @KafkaListener(topics = "orders", groupId = "orders")
    public void consume(Order order) {
        log.info("Order received to process: {}", order);
        orderRepository.findById(order.getId())
            .map(o -> o.setOrderStatus(order.getOrderStatus())
                .setResponseMessage(order.getResponseMessage()))
            .flatMap(orderRepository::save)
            .subscribe(
                saved -> {
                    OrderStatus next = NEXT_STATUS.get(order.getOrderStatus());
                    if (next != null) {
                        Order outbound = new Order();
                        outbound.setId(saved.getId());
                        outbound.setOrderStatus(next);
                        orderProducer.sendMessage(outbound);
                    }
                },
                err -> log.error("Failed to process order {} for status {}", order.getId(), order.getOrderStatus(), err));
    }
}