const ORDER_SERVICE_URL = 'http://localhost:8080/api/orders';
const INVENTORY_SERVICE_URL = 'http://localhost:8081/api/products';

export function fetchPaymentModes() {
  return ['Cash on Delivery', 'Card on Delivery'];
}

export async function fetchProducts() {
  try {
    const response = await fetch(INVENTORY_SERVICE_URL);
    if (!response.ok) throw new Error(`Request failed: ${response.status}`);
    return await response.json();
  } catch (err) {
    console.error('Failed to fetch products', err);
    return [];
  }
}

export async function createOrder(orderPayload) {
  const response = await fetch(ORDER_SERVICE_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(orderPayload),
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error((data && data.message) || `Request failed: ${response.status}`);
  }
  return data;
}

export async function fetchOrders() {
  const response = await fetch(ORDER_SERVICE_URL);
  if (!response.ok) throw new Error(`Request failed: ${response.status}`);
  return await response.json();
}
