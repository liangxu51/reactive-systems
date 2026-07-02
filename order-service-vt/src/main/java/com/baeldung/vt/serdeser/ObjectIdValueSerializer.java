package com.baeldung.vt.serdeser;

import org.bson.types.ObjectId;
import org.springframework.stereotype.Component;

import tools.jackson.core.JsonGenerator;
import tools.jackson.databind.SerializationContext;
import tools.jackson.databind.ValueSerializer;

@Component
public class ObjectIdValueSerializer extends ValueSerializer<ObjectId> {

    @Override
    public void serialize(ObjectId value, JsonGenerator gen, SerializationContext context) {
        gen.writeString(value.toString());
    }
}
