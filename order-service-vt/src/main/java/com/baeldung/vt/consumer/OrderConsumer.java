package com.baeldung.vt.consumer;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.producer.OrderProducer;
import com.baeldung.vt.repository.OrderRepository;

import lombok.extern.slf4j.Slf4j;

@Slf4j
@Service
public class OrderConsumer {

    @Autowired
    private OrderRepository orderRepository;

    @Autowired
    private OrderProducer orderProducer;

    @KafkaListener(topics = "orders", groupId = "orders-vt")
    public void consume(Order order) {
        log.info("Order received to process: {}", order);
        orderRepository.findById(order.getId()).ifPresent(o -> {
            if (OrderStatus.INITIATION_SUCCESS.equals(order.getOrderStatus())) {
                orderProducer.sendMessage(o.setOrderStatus(OrderStatus.RESERVE_INVENTORY));
            } else if (OrderStatus.INVENTORY_SUCCESS.equals(order.getOrderStatus())) {
                orderProducer.sendMessage(o.setOrderStatus(OrderStatus.PREPARE_SHIPPING));
            } else if (OrderStatus.SHIPPING_FAILURE.equals(order.getOrderStatus())) {
                orderProducer.sendMessage(o.setOrderStatus(OrderStatus.REVERT_INVENTORY));
            }
            orderRepository.save(o.setOrderStatus(order.getOrderStatus())
                .setResponseMessage(order.getResponseMessage()));
        });
    }

}
