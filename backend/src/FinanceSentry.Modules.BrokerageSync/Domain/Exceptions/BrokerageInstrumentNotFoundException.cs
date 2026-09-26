using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

public sealed class BrokerageInstrumentNotFoundException(string message)
    : ApiException(404, "INSTRUMENT_NOT_FOUND", message);
