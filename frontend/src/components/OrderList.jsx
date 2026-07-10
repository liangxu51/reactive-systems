function statusVariant(orderStatus) {
  if (!orderStatus) return '';
  if (orderStatus.includes('SUCCESS')) return 'success';
  if (orderStatus.includes('FAILURE')) return 'failure';
  return '';
}

export default function OrderList({ orders, onGetPreviousOrders, onGetOrderStream }) {
  return (
    <div className="card orders-card">
      <div className="card-header-row">
        <h3 className="card-title">Order History</h3>
        <div className="action-row">
          <button className="btn btn-outline-action" onClick={onGetPreviousOrders}>
            Get Previous Orders
          </button>
          <button className="btn btn-outline-action" onClick={onGetOrderStream}>
            Get Previous Order Stream
          </button>
        </div>
      </div>
      {orders === null ? (
        <p className="empty-state">Choose an option above to load your orders.</p>
      ) : orders.length === 0 ? (
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
