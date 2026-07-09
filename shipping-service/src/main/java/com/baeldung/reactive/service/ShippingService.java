package com.baeldung.reactive.service;

import java.time.Clock;
import java.time.LocalDate;
import java.time.LocalTime;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.domain.Shipment;
import com.baeldung.reactive.repository.ShipmentRepository;

import lombok.extern.slf4j.Slf4j;
import reactor.core.publisher.Mono;

@Slf4j
@Service
public class ShippingService {

    private static final LocalTime SHIPPING_WINDOW_START = LocalTime.of(10, 0);
    private static final LocalTime SHIPPING_WINDOW_END = LocalTime.of(18, 0);

    @Autowired
    ShipmentRepository shipmentRepository;

    @Autowired
    Clock clock;

    public Mono<Order> handleOrder(Order order) {
        log.info("Handle order invoked with: {}", order);
        return Mono.just(order)
            .flatMap(o -> {
                LocalTime now = LocalTime.now(clock);
                if (now.isAfter(SHIPPING_WINDOW_START) && now.isBefore(SHIPPING_WINDOW_END)) {
                    LocalDate shippingDate = LocalDate.now(clock).plusDays(1);
                    return shipmentRepository.save(new Shipment().setAddress(order.getShippingAddress())
                        .setShippingDate(shippingDate));
                } else {
                    return Mono.error(new RuntimeException("The current time is off the limits to place order."));
                }
            })
            .map(s -> order.setShippingDate(s.getShippingDate())
                .setOrderStatus(OrderStatus.SUCCESS));
    }

}