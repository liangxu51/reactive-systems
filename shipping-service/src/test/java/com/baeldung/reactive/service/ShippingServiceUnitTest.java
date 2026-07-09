package com.baeldung.reactive.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.time.Clock;
import java.time.LocalDate;
import java.time.ZoneId;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Address;
import com.baeldung.domain.Order;
import com.baeldung.domain.Shipment;
import com.baeldung.reactive.repository.ShipmentRepository;

import reactor.core.publisher.Mono;
import reactor.test.StepVerifier;

@ExtendWith(MockitoExtension.class)
class ShippingServiceUnitTest {

    private static final ZoneId ZONE = ZoneId.of("UTC");

    @Mock
    private ShipmentRepository shipmentRepository;

    @InjectMocks
    private ShippingService shippingService;

    private Order order;

    @BeforeEach
    void setUp() {
        order = new Order().setOrderStatus(OrderStatus.PREPARE_SHIPPING);
        order.setShippingAddress(new Address());
    }

    @Test
    void givenTimeWithinShippingWindow_whenHandleOrder_thenShipmentSavedAndOrderMarkedSuccess() {
        LocalDate today = LocalDate.of(2026, 7, 9);
        shippingService.clock = Clock.fixed(today.atTime(12, 0).atZone(ZONE).toInstant(), ZONE);

        when(shipmentRepository.save(any(Shipment.class)))
            .thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(shippingService.handleOrder(order))
            .assertNext(result -> {
                assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS);
                assertThat(result.getShippingDate()).isEqualTo(today.plusDays(1));
            })
            .verifyComplete();
    }

    @Test
    void givenTimeBeforeShippingWindow_whenHandleOrder_thenErrorsAndNoShipmentSaved() {
        LocalDate today = LocalDate.of(2026, 7, 9);
        shippingService.clock = Clock.fixed(today.atTime(9, 59).atZone(ZONE).toInstant(), ZONE);

        StepVerifier.create(shippingService.handleOrder(order))
            .expectErrorMessage("The current time is off the limits to place order.")
            .verify();

        verify(shipmentRepository, never()).save(any(Shipment.class));
    }

    @Test
    void givenTimeAfterShippingWindow_whenHandleOrder_thenErrorsAndNoShipmentSaved() {
        LocalDate today = LocalDate.of(2026, 7, 9);
        shippingService.clock = Clock.fixed(today.atTime(18, 0).atZone(ZONE).toInstant(), ZONE);

        StepVerifier.create(shippingService.handleOrder(order))
            .expectErrorMessage("The current time is off the limits to place order.")
            .verify();

        verify(shipmentRepository, never()).save(any(Shipment.class));
    }

    @Test
    void givenTimeAtWindowBoundaryStart_whenHandleOrder_thenTreatedAsOffLimits() {
        LocalDate today = LocalDate.of(2026, 7, 9);
        shippingService.clock = Clock.fixed(today.atTime(10, 0).atZone(ZONE).toInstant(), ZONE);

        StepVerifier.create(shippingService.handleOrder(order))
            .expectErrorMessage("The current time is off the limits to place order.")
            .verify();

        verify(shipmentRepository, never()).save(any(Shipment.class));
    }

}
