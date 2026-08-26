package com.baeldung.async.config;

import org.apache.kafka.clients.admin.NewTopic;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.kafka.config.TopicBuilder;

// Declares the "orders" topic explicitly instead of relying on the broker's
// auto.create.topics.enable default, which creates it with a single partition -
// capping every consumer group's parallelism at 1 no matter how many broker
// or service replicas exist (#43). 6 partitions (2x the 3-broker cluster) gives
// room to scale consumers without over-partitioning a demo-scale topic.
// Replication factor is left unset so it falls back to the broker's own
// default.replication.factor, which already differs correctly between the
// single-broker Testcontainers broker used for local runs and the 3-broker
// Helm cluster (#40).
//
// Spring's KafkaAdmin creates this topic if missing and raises its partition
// count on startup if an existing topic already has fewer - safe to declare
// identically in every service that shares this topic, the same way each
// service already keeps its own copy of KafkaConsumerConfig.
@Configuration
public class KafkaTopicConfig {

    @Bean
    public NewTopic ordersTopic() {
        return TopicBuilder.name("orders")
            .partitions(6)
            .build();
    }
}
