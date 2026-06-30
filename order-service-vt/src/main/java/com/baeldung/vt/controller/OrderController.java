package com.baeldung.vt.controller;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.MediaType;
import org.springframework.web.bind.annotation.CrossOrigin;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.SseEmitter;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.service.OrderService;

import lombok.extern.slf4j.Slf4j;

import java.util.List;

@Slf4j
@RestController
@CrossOrigin(origins = "*")
@RequestMapping("/api/orders")
public class OrderController {

    @Autowired
    private OrderService orderService;

    @PostMapping
    public Order create(@RequestBody Order order) {
        log.info("Create order invoked with: {}", order);
        Order created = orderService.createOrder(order);
        if (OrderStatus.FAILURE.equals(created.getOrderStatus())) {
            throw new RuntimeException("Order processing failed, please try again later. " + created.getResponseMessage());
        }
        return created;
    }

    // Consumed by the Angular reactive demo (Accept: text/event-stream).
    // Each order is emitted on its own virtual thread — no reactive types needed.
    @GetMapping(produces = MediaType.TEXT_EVENT_STREAM_VALUE)
    public SseEmitter stream() {
        log.info("SSE stream of orders invoked.");
        SseEmitter emitter = new SseEmitter(Long.MAX_VALUE);
        Thread.ofVirtual().start(() -> {
            try {
                for (Order order : orderService.getOrders()) {
                    emitter.send(order);
                }
                emitter.complete();
            } catch (Exception e) {
                emitter.completeWithError(e);
            }
        });
        return emitter;
    }

    @GetMapping(produces = MediaType.APPLICATION_JSON_VALUE)
    public List<Order> getAll() {
        log.info("Get all orders invoked.");
        return orderService.getOrders();
    }

}
