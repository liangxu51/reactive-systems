package com.baeldung.vt.domain;

import lombok.Data;

@Data
public class Address {

    private String name;
    private String house;
    private String street;
    private String city;
    private String zip;

    // SEC-004 (defense in depth): redact PII at the source so any class
    // that embeds an Address and logs it via the default Lombok
    // toString() - not just Order, which has its own explicit override -
    // never leaks name/house/street/city/zip.
    @Override
    public String toString() {
        return "Address[redacted]";
    }

}
