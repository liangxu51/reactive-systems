import { useEffect } from 'react';

// See ordersApi.js - relative path, proxied by nginx/Vite dev server.
const ORDERS_STREAM_URL = '/api/orders';

export function useOrderStream(active, onOrders) {
  useEffect(() => {
    if (!active) return;

    const orders = [];
    const eventSource = new EventSource(ORDERS_STREAM_URL);

    eventSource.onmessage = (event) => {
      orders.push(JSON.parse(event.data));
      onOrders([...orders]);
    };

    eventSource.onerror = () => {
      // GET /api/orders returns a finite snapshot Flux that completes and closes
      // the connection once every order has been sent. EventSource can't tell that
      // apart from a dropped connection - it fires onerror with readyState still
      // CONNECTING (not CLOSED) and retries forever unless we close it ourselves.
      // Only treat it as a genuine failure if we never got any data.
      if (orders.length === 0) {
        console.error('EventSource error while streaming orders');
      }
      eventSource.close();
    };

    return () => eventSource.close();
  }, [active, onOrders]);
}
