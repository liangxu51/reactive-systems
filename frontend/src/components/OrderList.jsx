export default function OrderList({ orders }) {
  if (orders === null) return null;
  return (
    <div className="container">
      <h2>Your orders placed so far:</h2>
      <ul>
        {orders.map((order) => (
          <li key={order.id}>
            <p>
              Order ID: {order.id}, Order Status: {order.orderStatus}, Order Message: {order.responseMessage}
            </p>
          </li>
        ))}
      </ul>
    </div>
  );
}
