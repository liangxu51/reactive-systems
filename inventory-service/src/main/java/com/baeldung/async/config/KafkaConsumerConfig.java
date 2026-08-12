package com.baeldung.async.config;

import org.springframework.boot.kafka.autoconfigure.ConcurrentKafkaListenerContainerFactoryConfigurer;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.kafka.config.ConcurrentKafkaListenerContainerFactory;
import org.springframework.kafka.core.ConsumerFactory;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.kafka.listener.DeadLetterPublishingRecoverer;
import org.springframework.kafka.listener.DefaultErrorHandler;
import org.springframework.util.backoff.FixedBackOff;

// Overrides Spring Boot's autoconfigured "kafkaListenerContainerFactory" bean so every
// @KafkaListener on the shared "orders" topic gets bounded retries and a dead-letter route
// instead of a poison-pill message silently dropping or wedging the consumer (see #19).
@Configuration
public class KafkaConsumerConfig {

    @Bean
    public DeadLetterPublishingRecoverer deadLetterPublishingRecoverer(KafkaTemplate<Object, Object> kafkaTemplate) {
        return new DeadLetterPublishingRecoverer(kafkaTemplate);
    }

    @Bean
    public DefaultErrorHandler kafkaErrorHandler(DeadLetterPublishingRecoverer recoverer) {
        return new DefaultErrorHandler(recoverer, new FixedBackOff(1000L, 3L));
    }

    @Bean
    public ConcurrentKafkaListenerContainerFactory<Object, Object> kafkaListenerContainerFactory(
        ConcurrentKafkaListenerContainerFactoryConfigurer configurer,
        ConsumerFactory<Object, Object> consumerFactory, DefaultErrorHandler kafkaErrorHandler) {
        ConcurrentKafkaListenerContainerFactory<Object, Object> factory = new ConcurrentKafkaListenerContainerFactory<>();
        // Applies every spring.kafka.listener.* property (including
        // observation-enabled, #46) to this factory the same way Spring
        // Boot's own autoconfigured bean would - a hand-built factory (#19)
        // otherwise silently ignores all of them. Confirmed live: without
        // this, spring.kafka.listener.observation-enabled=true had no
        // effect and @KafkaListener methods created no spans at all, with
        // no error anywhere.
        configurer.configure(factory, consumerFactory);
        factory.setCommonErrorHandler(kafkaErrorHandler);
        // Matches the "orders" topic's 6 partitions (#43) - each of these threads gets its
        // own partition assignment, so a single inventory-service pod can process up to 6
        // orders' worth of RESERVE_INVENTORY/REVERT_INVENTORY messages concurrently instead
        // of one poll loop handling every partition serially. Keep this <= partition count;
        // extra threads beyond that would just sit idle with no partition to claim.
        factory.setConcurrency(6);
        return factory;
    }
}
