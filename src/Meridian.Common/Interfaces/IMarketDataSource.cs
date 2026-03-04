using Meridian.Common.Models;

namespace Meridian.Common.Interfaces;

public interface IMarketDataSource
{
    IObservable<MarketTick> GetTickStream(string symbol);
    IObservable<MarketTick> GetAllTicksStream();
}
