export default function OrderForm({
  orderForm,
  paymentModes,
  onPaymentModeChange,
  onLineItemQuantityChange,
  onAddressFieldChange,
  onSubmit,
}) {
  return (
    <div className="container">
      <form onSubmit={onSubmit}>
        <h3 className="container">Product Quantities:</h3>
        {orderForm.lineItems.map((item) => (
          <div className="container" key={item.productId}>
            <li className="form-group input-group-lg">
              {item.name}:{' '}
              <input
                value={item.quantity}
                placeholder="10"
                onChange={(e) => onLineItemQuantityChange(item.productId, e.target.value)}
              />
              <p>Only {item.stock} left in the stock!</p>
            </li>
          </div>
        ))}
        <div className="container">
          <h3>
            Payment Mode:
            <select
              className="form-group input-group-lg"
              value={orderForm.paymentMode}
              onChange={(e) => onPaymentModeChange(e.target.value)}
            >
              {paymentModes.map((mode) => (
                <option key={mode} value={mode}>
                  {mode}
                </option>
              ))}
            </select>
          </h3>
        </div>
        <div className="container">
          <h3>Address:</h3>
          <input
            className="form-control"
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
        <div className="form-group">
          <button className="btn btn-danger btn-block btn-lg" type="submit">
            Place Order
          </button>
        </div>
      </form>
    </div>
  );
}
