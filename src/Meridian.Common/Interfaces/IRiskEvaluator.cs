using Meridian.Common.Models;

namespace Meridian.Common.Interfaces;

public interface IRiskEvaluator
{
    IObservable<RiskAlert> MonitorRisk(IObservable<PortfolioSnapshot> portfolio);
}
