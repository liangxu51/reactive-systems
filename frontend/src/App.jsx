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
    <>
      <div className="container">
        <h2>Please place a new Order!</h2>
      </div>
      {response !== null && (
        <div className="container">
          <h3>Your order {response.id} was successfully placed, please check the status of order.</h3>
        </div>
      )}
      {error !== null && (
        <div className="container">
          <h3>Your order could not be placed at the moment: {error.message}</h3>
        </div>
      )}
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
      <div className="container">
        <button className="btn btn-danger btn-block btn-lg" onClick={handleGetPreviousOrders}>
          Get Previous Orders
        </button>
      </div>
      <div className="container">
        <button className="btn btn-danger btn-block btn-lg" onClick={handleGetOrderStream}>
          Get Previous Order Stream
        </button>
      </div>
      <OrderList orders={previousOrders} />
    </>
  );
}
