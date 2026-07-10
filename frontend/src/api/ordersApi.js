// Relative paths - nginx (production/Docker/Helm) or the Vite dev server
// proxies these to the actual order-service/inventory-service addresses,
// so the frontend never needs to know a deployment-specific hostname
// (see ASSESSMENT.md tech debt #4).
const ORDER_SERVICE_URL = '/api/orders';
const INVENTORY_SERVICE_URL = '/api/products';

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
