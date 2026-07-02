package com.baeldung.vt.domain;

import org.bson.types.ObjectId;

import com.baeldung.vt.serdeser.ObjectIdSerializer;
import com.baeldung.vt.serdeser.ObjectIdValueSerializer;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.databind.annotation.JsonSerialize;

import lombok.Data;

@Data
@JsonIgnoreProperties(ignoreUnknown = true)
public class LineItem {

    // Spring Kafka's JSON (de)serializer still runs on Jackson 2, while Spring MVC runs on Jackson 3 - both annotations are needed.
    @JsonSerialize(using = ObjectIdSerializer.class)
    @tools.jackson.databind.annotation.JsonSerialize(using = ObjectIdValueSerializer.class)
    private ObjectId productId;
    private int quantity;

    public LineItem setProductId(ObjectId productId) {
        this.productId = productId;
        return this;
    }

    public LineItem setQuantity(int quantity) {
        this.quantity = quantity;
        return this;
    }

}
