package com.baeldung.reactive.repository;

import org.springframework.data.mongodb.repository.ReactiveMongoRepository;

import com.baeldung.domain.ProcessedEvent;

public interface ProcessedEventRepository extends ReactiveMongoRepository<ProcessedEvent, String> {

}
