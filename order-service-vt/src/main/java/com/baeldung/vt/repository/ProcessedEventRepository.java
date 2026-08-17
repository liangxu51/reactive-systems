package com.baeldung.vt.repository;

import org.springframework.data.mongodb.repository.MongoRepository;

import com.baeldung.vt.domain.ProcessedEvent;

public interface ProcessedEventRepository extends MongoRepository<ProcessedEvent, String> {

}
