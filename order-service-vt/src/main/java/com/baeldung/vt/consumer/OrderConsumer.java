package com.baeldung.vt.consumer;

import org.slf4j.MDC;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.domain.ProcessedEvent;
import com.baeldung.vt.producer.OrderProducer;
import com.baeldung.vt.repository.OrderRepository;
import com.baeldung.vt.repository.ProcessedEventRepository;

import lombok.extern.slf4j.Slf4j;

@Slf4j
@Service
public class OrderConsumer {

    @Autowired
    private OrderRepository orderRepository;

    @Autowired
    private OrderProducer orderProducer;

    @Autowired
    private ProcessedEventRepository processedEventRepository;

    @KafkaListener(topics = "orders", groupId = "orders-vt")
    public void consume(Order order) {
        // Blocking listener thread, whole method runs synchronously - a plain
        // ThreadLocal-based MDC scope covers every log line here (#47).
        try (var ignored = MDC.putCloseable("orderId", order.getId().toHexString())) {
            log.info("Order received to process: {}", order);
            // Issue #48: dedup insert first, keyed on (orderId, status) - the Mongo status
            // save below is idempotent-by-value on its own, but a redelivered message's
            // re-publish is not (a redelivered INVENTORY_SUCCESS would double-fire
            // PREPARE_SHIPPING, a redelivered SHIPPING_FAILURE would double-fire
            // REVERT_INVENTORY).
            String dedupId = order.getId().toHexString() + ":" + order.getOrderStatus();
            try {
                processedEventRepository.insert(new ProcessedEvent(dedupId));
            } catch (DuplicateKeyException e) {
                log.info("Duplicate {} event for order {}, already processed - skipping.", order.getOrderStatus(), order.getId());
                return;
            }
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

}
