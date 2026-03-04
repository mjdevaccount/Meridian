using Meridian.Common.Models;

namespace Meridian.Common.Interfaces;

public interface IPricingModel
{
    string ModelName { get; }
    PricingResult Price(Option option, MarketSnapshot market);
    GreeksResult ComputeGreeks(Option option, MarketSnapshot market);
}
