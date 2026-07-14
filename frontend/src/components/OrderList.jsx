function statusVariant(orderStatus) {
  if (!orderStatus) return '';
  if (orderStatus.includes('SUCCESS')) return 'success';
  if (orderStatus.includes('FAILURE')) return 'failure';
  return '';
}

// A Mongo ObjectId's first 4 bytes are a big-endian creation-time timestamp
// (seconds since epoch) - Order has no dedicated "created at" field, so this
// is the only reliable per-order timestamp available on both list and stream.
function orderTimestamp(id) {
  return parseInt(id.substring(0, 8), 16) * 1000;
}

function formatOrderDate(id) {
  return new Date(orderTimestamp(id)).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

export default function OrderList({ orders, onGetPreviousOrders, onGetOrderStream }) {
  const sortedOrders = orders === null ? null : [...orders].sort((a, b) => orderTimestamp(b.id) - orderTimestamp(a.id));

  return (
    <div className="card orders-card">
      <div className="card-header-row">
        <h3 className="card-title">
          Order History
          {sortedOrders !== null && <span className="order-count">{sortedOrders.length}</span>}
        </h3>
        <div className="action-row">
          <button className="btn btn-outline-action" onClick={onGetPreviousOrders}>
            Get Previous Orders
          </button>
          <button className="btn btn-outline-action" onClick={onGetOrderStream}>
            Get Previous Order Stream
          </button>
        </div>
      </div>
      {sortedOrders === null ? (
        <p className="empty-state">Choose an option above to load your orders.</p>
      ) : sortedOrders.length === 0 ? (
        <p className="empty-state">No orders yet.</p>
      ) : (
        <ul className="order-list">
          {sortedOrders.map((order) => (
            <li className="order-row" key={order.id}>
              <div className="order-row-main">
                <span className="order-id">Order {order.id}</span>
                <span className="order-date">{formatOrderDate(order.id)}</span>
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
