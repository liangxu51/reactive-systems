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
        try {
            order.setLineItems(order.getLineItems()
                .stream()
                .filter(l -> l.getQuantity() > 0)
                .collect(Collectors.toList()));
            Order saved = orderRepository.save(order);
            orderProducer.sendMessage(saved.setOrderStatus(OrderStatus.INITIATION_SUCCESS));
            return orderRepository.save(saved);
        } catch (Exception e) {
            return orderRepository.save(order
                .setOrderStatus(OrderStatus.FAILURE)
                .setResponseMessage(e.getMessage()));
        }
    }

    public List<Order> getOrders() {
        log.info("Get all orders invoked.");
        return orderRepository.findAll();
    }

}
