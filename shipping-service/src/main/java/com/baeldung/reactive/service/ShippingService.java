package com.baeldung.reactive.service;

import java.time.Clock;
import java.time.LocalDate;
import java.time.LocalTime;
import java.util.Date;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;
import com.baeldung.domain.ProcessedEvent;
import com.baeldung.domain.Shipment;
import com.baeldung.reactive.repository.ProcessedEventRepository;
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
    ProcessedEventRepository processedEventRepository;

    @Autowired
    Clock clock;

    // Issue #48: inserted first, inside the same transaction as the shipment
    // insert below - a redelivered PREPARE_SHIPPING hits the unique _id
    // index and throws DuplicateKeyException before ever creating a second
    // shipment (Shipment has no orderId field, so nothing else here could
    // otherwise tell a redelivery apart from a genuinely new order).
    @Transactional
    public Mono<Order> handleOrder(Order order) {
        log.info("Handle order invoked with: {}", order);
        String dedupId = order.getId().toHexString() + ":" + order.getOrderStatus();
        return processedEventRepository.insert(new ProcessedEvent(dedupId))
            .then(Mono.defer(() -> {
                LocalTime now = LocalTime.now(clock);
                if (now.isAfter(SHIPPING_WINDOW_START) && now.isBefore(SHIPPING_WINDOW_END)) {
                    LocalDate shippingDate = LocalDate.now(clock).plusDays(1);
                    return shipmentRepository.save(new Shipment().setAddress(order.getShippingAddress())
                        .setShippingDate(shippingDate));
                } else {
                    return Mono.error(new RuntimeException("The current time is off the limits to place order."));
                }
            }))
            .map(s -> order.setShippingDate(Date.from(s.getShippingDate().atStartOfDay(clock.getZone()).toInstant()))
                .setOrderStatus(OrderStatus.SUCCESS));
        // DuplicateKeyException from the dedup insert above deliberately propagates out of
        // this @Transactional method uncaught - Spring's reactive transaction interceptor
        // only rolls back on an error signal. Catching it here and completing empty instead
        // would make the method return successfully, and Spring would then try to COMMIT a
        // transaction that already had a failed write in it, which MongoDB rejects (and
        // rejects with a transient-transaction-error label - confirmed live). The dedup
        // check in OrderConsumer catches this same DuplicateKeyException *after* this
        // method returns, once the transaction has already been safely rolled back.
    }

}