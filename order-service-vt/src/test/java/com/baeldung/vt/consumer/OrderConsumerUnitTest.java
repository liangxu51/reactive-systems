package com.baeldung.vt.consumer;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.doAnswer;
import static org.mockito.Mockito.lenient;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;
import java.util.Optional;

import org.bson.types.ObjectId;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.dao.DuplicateKeyException;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.Address;
import com.baeldung.vt.domain.LineItem;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.domain.ProcessedEvent;
import com.baeldung.vt.producer.OrderProducer;
import com.baeldung.vt.repository.OrderRepository;
import com.baeldung.vt.repository.ProcessedEventRepository;

@ExtendWith(MockitoExtension.class)
class OrderConsumerUnitTest {

    @Mock
    private OrderRepository orderRepository;

    @Mock
    private OrderProducer orderProducer;

    @Mock
    private ProcessedEventRepository processedEventRepository;

    @InjectMocks
    private OrderConsumer orderConsumer;

    private ObjectId orderId;
    private Order existing;

    @BeforeEach
    void setUp() {
        orderId = new ObjectId();
        existing = new Order();
        existing.setId(orderId);
        existing.setUserId("user-1");
        existing.setTotal(100L);
        existing.setLineItems(List.of(new LineItem().setProductId(new ObjectId()).setQuantity(2)));
        Address address = new Address();
        address.setName("Test User");
        address.setCity("Testville");
        existing.setShippingAddress(address);

        // lenient: the duplicate-event test below short-circuits before either of these
        // is ever reached, which would otherwise make Mockito's strict stubbing flag them
        // as unused there.
        lenient().when(orderRepository.findById(orderId)).thenReturn(Optional.of(existing));
        lenient().when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> invocation.getArgument(0));
    }

    @Test
    void givenInitiationSuccess_whenConsume_thenNextStatusMessagePublished() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INITIATION_SUCCESS);

        orderConsumer.consume(incoming);

        verify(orderProducer, times(1)).sendMessage(any(Order.class));
    }

    @Test
    void givenInitiationSuccess_whenConsume_thenOutboundMessageCarriesFullOrderData() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INITIATION_SUCCESS);

        // Unlike order-service's reactive consumer, this blocking one publishes with the
        // SAME mutable `o` it later re-mutates for orderRepository.save() (with the incoming
        // status, after the publish) - so an ArgumentCaptor read post-consume() would see
        // that later mutation, not the RESERVE_INVENTORY status the message actually carried
        // when sendMessage() was called. Snapshotting eagerly avoids that.
        OrderStatus[] statusAtSendTime = new OrderStatus[1];
        doAnswer(invocation -> {
            statusAtSendTime[0] = ((Order) invocation.getArgument(0)).getOrderStatus();
            return null;
        }).when(orderProducer).sendMessage(any(Order.class));

        orderConsumer.consume(incoming);

        ArgumentCaptor<Order> outboundCaptor = ArgumentCaptor.forClass(Order.class);
        verify(orderProducer).sendMessage(outboundCaptor.capture());
        Order outbound = outboundCaptor.getValue();

        assertThat(statusAtSendTime[0]).isEqualTo(OrderStatus.RESERVE_INVENTORY);
        assertThat(outbound.getId()).isEqualTo(orderId);
        assertThat(outbound.getUserId()).isEqualTo(existing.getUserId());
        assertThat(outbound.getTotal()).isEqualTo(existing.getTotal());
        assertThat(outbound.getLineItems()).isEqualTo(existing.getLineItems());
        assertThat(outbound.getShippingAddress()).isEqualTo(existing.getShippingAddress());
    }

    @Test
    void givenInventoryFailure_whenConsume_thenOrderStatusSavedButNoFurtherMessagePublished() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INVENTORY_FAILURE);
        incoming.setResponseMessage("Product is out of stock");

        orderConsumer.consume(incoming);

        verify(orderRepository).save(existing);
        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

    @Test
    void givenDuplicateEvent_whenConsume_thenNoSaveAndNoMessagePublished() {
        when(processedEventRepository.insert(any(ProcessedEvent.class))).thenThrow(new DuplicateKeyException("duplicate key"));

        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INITIATION_SUCCESS);

        orderConsumer.consume(incoming);

        verify(orderRepository, never()).save(any(Order.class));
        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

}
