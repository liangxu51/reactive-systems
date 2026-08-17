import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

// Seeded by the Helm chart README's "Seed product data" step - both start
// at stock: 100, so a sustained run exhausts them fast (see load-test/README.md
// for how to bump stock before a real throughput run).
const PRODUCT_IDS = ['5edcbfd30717397ae8cfb7f0', '5edcbfd30717397ae8cfb7f1'];

const BASE_URL = __ENV.BASE_URL || 'http://order-service:8080';
const API_USERNAME = __ENV.API_USERNAME || 'admin';
const API_PASSWORD = __ENV.API_PASSWORD;

const ordersCreated = new Counter('orders_created');
const ordersRejected = new Counter('orders_rejected');
const createOrderDuration = new Trend('order_create_duration', true);

export const options = {
  scenarios: {
    order_saga: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: __ENV.RAMP_UP || '30s', target: Number(__ENV.TARGET_VUS || 10) },
        { duration: __ENV.HOLD || '2m', target: Number(__ENV.TARGET_VUS || 10) },
        { duration: __ENV.RAMP_DOWN || '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    order_create_duration: ['p(95)<2000'],
  },
};

function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

function randomOrderPayload() {
  const lineItemCount = randomInt(1, 2);
  const usedProducts = new Set();
  const lineItems = [];
  while (lineItems.length < lineItemCount) {
    const productId = PRODUCT_IDS[randomInt(0, PRODUCT_IDS.length - 1)];
    if (usedProducts.has(productId)) continue;
    usedProducts.add(productId);
    lineItems.push({ productId, quantity: randomInt(1, 2) });
  }

  return {
    userId: `load-test-vu${__VU}-iter${__ITER}`,
    lineItems,
    total: lineItems.length * 10,
    paymentMode: 'CARD',
    shippingAddress: {
      name: 'Load Test',
      house: '1',
      street: 'Test St',
      city: 'Testville',
      zip: '00000',
    },
  };
}

export default function () {
  const payload = JSON.stringify(randomOrderPayload());
  const params = {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'CreateOrder' },
  };

  // k6 reads HTTP Basic auth from the URL's userinfo segment, not a params field.
  const url = API_PASSWORD
    ? BASE_URL.replace('://', `://${API_USERNAME}:${API_PASSWORD}@`) + '/api/orders'
    : `${BASE_URL}/api/orders`;

  const res = http.post(url, payload, params);
  createOrderDuration.add(res.timings.duration);

  const ok = check(res, {
    'order accepted (2xx)': (r) => r.status >= 200 && r.status < 300,
  });

  if (ok) {
    ordersCreated.add(1);
  } else {
    ordersRejected.add(1);
  }

  sleep(randomInt(1, 3));
}
