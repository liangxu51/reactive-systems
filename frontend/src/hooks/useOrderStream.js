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
      if (eventSource.readyState === EventSource.CLOSED) {
        eventSource.close();
      } else {
        console.error('EventSource error while streaming orders');
      }
    };

    return () => eventSource.close();
  }, [active, onOrders]);
}
