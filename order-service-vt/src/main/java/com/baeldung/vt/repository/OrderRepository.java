package com.baeldung.vt.repository;

import org.bson.types.ObjectId;
import org.springframework.data.mongodb.repository.MongoRepository;

import com.baeldung.vt.domain.Order;

public interface OrderRepository extends MongoRepository<Order, ObjectId> {

}
