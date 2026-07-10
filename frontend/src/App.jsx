import { useState, useEffect } from 'react';
import OrderForm from './components/OrderForm';
import OrderList from './components/OrderList';
import { fetchPaymentModes, fetchProducts, createOrder, fetchOrders } from './api/ordersApi';
import { useOrderStream } from './hooks/useOrderStream';

function buildInitialOrderForm(products, paymentModes) {
  return {
    userId: 'Bob Marley',
    paymentMode: paymentModes[0],
    lineItems: products.map((product) => ({
      productId: product.id,
      name: product.name,
      stock: product.stock,
      quantity: 10,
    })),
    shippingAddress: {
      name: 'Bob Marley',
      house: '24',
      street: 'Ashford Av.',
      city: 'New York',
      zip: '11001',
    },
  };
}

export default function App() {
  const [paymentModes] = useState(() => fetchPaymentModes());
  const [orderForm, setOrderForm] = useState(null);
  const [response, setResponse] = useState(null);
  const [error, setError] = useState(null);
  const [previousOrders, setPreviousOrders] = useState(null);
  const [streamActive, setStreamActive] = useState(false);

  useEffect(() => {
    fetchProducts().then((fetchedProducts) => {
      setOrderForm(buildInitialOrderForm(fetchedProducts, paymentModes));
    });
  }, [paymentModes]);

  useOrderStream(streamActive, setPreviousOrders);

  function handlePaymentModeChange(value) {
    setOrderForm((prev) => ({ ...prev, paymentMode: value }));
  }

  function handleLineItemQuantityChange(productId, quantity) {
    setOrderForm((prev) => ({
      ...prev,
      lineItems: prev.lineItems.map((item) =>
        item.productId === productId ? { ...item, quantity } : item
      ),
    }));
  }

  function handleAddressFieldChange(field, value) {
    setOrderForm((prev) => ({
      ...prev,
      shippingAddress: { ...prev.shippingAddress, [field]: value },
    }));
  }

  async function handleSubmit(event) {
    event.preventDefault();
    try {
      const createdOrder = await createOrder(orderForm);
      setResponse(createdOrder);
      setError(null);
    } catch (err) {
      setError(err);
      setResponse(null);
    }
  }

  async function handleGetPreviousOrders() {
    setStreamActive(false);
    try {
      const orders = await fetchOrders();
      setPreviousOrders(orders);
    } catch (err) {
      console.error('Failed to fetch orders', err);
    }
  }

  function handleGetOrderStream() {
    setPreviousOrders([]);
    setStreamActive(true);
  }

  return (
    <div className="page">
      <header className="page-header">
        <h1>Reactive Order Demo</h1>
        <p>Place an order and watch it flow through the reactive saga.</p>
      </header>

      <main className="page-content">
        {response !== null && (
          <div className="alert-banner success">
            Your order {response.id} was successfully placed. Check the order status below.
          </div>
        )}
        {error !== null && (
          <div className="alert-banner error">Your order could not be placed at the moment: {error.message}</div>
        )}

        <div className="workspace">
          {orderForm !== null && (
            <OrderForm
              orderForm={orderForm}
              paymentModes={paymentModes}
              onPaymentModeChange={handlePaymentModeChange}
              onLineItemQuantityChange={handleLineItemQuantityChange}
              onAddressFieldChange={handleAddressFieldChange}
              onSubmit={handleSubmit}
            />
          )}

          <OrderList
            orders={previousOrders}
            onGetPreviousOrders={handleGetPreviousOrders}
            onGetOrderStream={handleGetOrderStream}
          />
        </div>
      </main>
    </div>
  );
}
