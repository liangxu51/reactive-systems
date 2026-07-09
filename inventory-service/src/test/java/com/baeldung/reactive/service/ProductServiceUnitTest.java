package com.baeldung.reactive.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import org.bson.types.ObjectId;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.LineItem;
import com.baeldung.domain.Order;
import com.baeldung.domain.Product;
import com.baeldung.reactive.repository.ProductRepository;

import reactor.core.publisher.Mono;
import reactor.test.StepVerifier;

@ExtendWith(MockitoExtension.class)
class ProductServiceUnitTest {

    @Mock
    private ProductRepository productRepository;

    @InjectMocks
    private ProductService productService;

    private ObjectId productId;
    private Product product;
    private Order order;

    @BeforeEach
    void setUp() {
        productId = new ObjectId();
        product = new Product();
        product.setId(productId);
        product.setStock(5);

        order = new Order();
        order.setLineItems(List.of(
            new LineItem().setProductId(productId).setQuantity(3)));
    }

    @Test
    void givenSufficientStock_whenHandleOrder_thenStockIsDecrementedAndOrderMarkedSuccess() {
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));
        when(productRepository.save(any(Product.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(productService.handleOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS))
            .verifyComplete();

        assertThat(product.getStock()).isEqualTo(2);
        verify(productRepository).save(product);
    }

    @Test
    void givenInsufficientStock_whenHandleOrder_thenErrorsAndStockIsUnchanged() {
        product.setStock(1);
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));

        StepVerifier.create(productService.handleOrder(order))
            .expectErrorMessage("Product is out of stock: " + productId)
            .verify();

        assertThat(product.getStock()).isEqualTo(1);
        verify(productRepository, never()).save(any(Product.class));
    }

    @Test
    void givenExactStockMatchesQuantity_whenHandleOrder_thenStockReachesZeroAndOrderMarkedSuccess() {
        product.setStock(3);
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));
        when(productRepository.save(any(Product.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(productService.handleOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS))
            .verifyComplete();

        assertThat(product.getStock()).isEqualTo(0);
    }

    @Test
    void givenReservedOrder_whenRevertOrder_thenStockIsRestoredAndOrderMarkedSuccess() {
        product.setStock(2);
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));
        when(productRepository.save(any(Product.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(productService.revertOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS))
            .verifyComplete();

        assertThat(product.getStock()).isEqualTo(5);
        verify(productRepository).save(product);
    }

}
