using System.Runtime.CompilerServices;

// Lets order-service-cs.Tests call a small number of `internal` members
// (currently just OrderConsumer.ProcessWithRetry) directly instead of via
// reflection, the same way OrderConsumer.TryExtractParentContext is already
// `public` purely for testability - see OrderConsumer.cs.
[assembly: InternalsVisibleTo("OrderService.Api.Tests")]
