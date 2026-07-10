function statusVariant(orderStatus) {
  if (!orderStatus) return '';
  if (orderStatus.includes('SUCCESS')) return 'success';
  if (orderStatus.includes('FAILURE')) return 'failure';
  return '';
}

export default function OrderList({ orders }) {
  if (orders === null) return null;
  return (
    <div className="card">
      <h3 className="card-title">Your orders placed so far</h3>
      {orders.length === 0 ? (
        <p className="empty-state">No orders yet.</p>
      ) : (
        <ul className="order-list">
          {orders.map((order) => (
            <li className="order-row" key={order.id}>
              <div className="order-row-main">
                <span className="order-id">Order {order.id}</span>
                <span className="order-message">{order.responseMessage}</span>
              </div>
              <span className={`status-pill ${statusVariant(order.orderStatus)}`}>{order.orderStatus}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
