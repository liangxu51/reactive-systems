package com.baeldung.domain;

import org.bson.types.ObjectId;
import org.springframework.data.mongodb.core.mapping.Document;

import com.baeldung.serdeser.ObjectIdSerializer;
import com.baeldung.serdeser.ObjectIdValueSerializer;
import com.fasterxml.jackson.databind.annotation.JsonSerialize;

import lombok.Data;

@Data
@Document
public class Product {

    // Spring Kafka's JSON (de)serializer still runs on Jackson 2, while Spring WebFlux runs on Jackson 3 - both annotations are needed.
    @JsonSerialize(using = ObjectIdSerializer.class)
    @tools.jackson.databind.annotation.JsonSerialize(using = ObjectIdValueSerializer.class)
    private ObjectId id;
    private String name;
    private Long price;
    private Integer stock;

}
