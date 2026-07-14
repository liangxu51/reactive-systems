export default function OrderForm({
  orderForm,
  paymentModes,
  onPaymentModeChange,
  onLineItemQuantityChange,
  onAddressFieldChange,
  onSubmit,
}) {
  return (
    <div className="card">
      <form onSubmit={onSubmit}>
        <h3 className="card-title">Product Quantities</h3>
        <ul className="line-items">
          {orderForm.lineItems.map((item) => (
            <li className="line-item" key={item.productId}>
              <div className="line-item-info">
                <span className="line-item-name">{item.name}</span>
                <span className="line-item-stock">Only {item.stock} left in stock</span>
              </div>
              <input
                className="form-control line-item-qty"
                value={item.quantity}
                placeholder="10"
                onChange={(e) => onLineItemQuantityChange(item.productId, e.target.value)}
              />
            </li>
          ))}
        </ul>

        <div className="field-group">
          <label className="field-label" htmlFor="paymentMode">
            Payment Mode
          </label>
          <select
            id="paymentMode"
            className="form-control"
            value={orderForm.paymentMode}
            onChange={(e) => onPaymentModeChange(e.target.value)}
          >
            {paymentModes.map((mode) => (
              <option key={mode} value={mode}>
                {mode}
              </option>
            ))}
          </select>
        </div>

        <div className="field-group">
          <label className="field-label">Shipping Address</label>
          <div className="address-grid">
            <input
              className="form-control span-2"
              placeholder="Name"
              value={orderForm.shippingAddress.name}
              onChange={(e) => onAddressFieldChange('name', e.target.value)}
            />
            <input
              className="form-control"
              placeholder="House"
              value={orderForm.shippingAddress.house}
              onChange={(e) => onAddressFieldChange('house', e.target.value)}
            />
            <input
              className="form-control"
              placeholder="Street"
              value={orderForm.shippingAddress.street}
              onChange={(e) => onAddressFieldChange('street', e.target.value)}
            />
            <input
              className="form-control"
              placeholder="City"
              value={orderForm.shippingAddress.city}
              onChange={(e) => onAddressFieldChange('city', e.target.value)}
            />
            <input
              className="form-control"
              placeholder="Zip"
              value={orderForm.shippingAddress.zip}
              onChange={(e) => onAddressFieldChange('zip', e.target.value)}
            />
          </div>
        </div>

        <button className="btn btn-primary-solid btn-block btn-lg" type="submit">
          Place Order
        </button>
      </form>
    </div>
  );
}
